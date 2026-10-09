using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Connectivity;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Spectating.Session;

/// <summary>Why a stream hold ended.</summary>
public enum StreamHoldEnd
{
    /// <summary>The stream is live again: the next replay loads.</summary>
    Live,

    /// <summary>The hold no longer applies: connectivity is down (outage mode), the setting or the arm changed.</summary>
    NotDesired,

    /// <summary>A new release is staged; this install hands off to it.</summary>
    ReleaseStaged,

    /// <summary>Spectate is stopping.</summary>
    Stopped,
}

/// <summary>Whether the next replay waits for the stream, and why.</summary>
public sealed record StreamHoldCheck(bool Holds, ObsStreamHealth Health, string Reason)
{
    public static StreamHoldCheck Free(string reason, ObsStreamHealth health = null) =>
        new(false, health, reason);
}

/// <summary>
/// Holds the next replay while the desired stream is down (#396). On 2026-10-09 production played
/// replay after replay into a stream that was off air for 4 h 11 min. Between replays, when
/// <c>Spectate:HoldWhileStreamDown</c> is on and the stream is desired (<c>OBS:Enabled</c>,
/// <c>OBS:StreamingEnabled</c>, this machine armed, connectivity online) but not
/// <see cref="ObsStreamState.Live"/> (inactive, reconnecting, stalled, or OBS not answering),
/// spectate loads no replay. It shows the waiting scene, writes phase <c>StreamHold</c> to
/// status.json, and waits with no time limit while the stream reconcile (#395) keeps recovering
/// the stream. A match already on screen is never held. An outage has its own mode and is not a
/// hold. The hold is not spectate failure: the heartbeat carries it, so the supervisor and the
/// release gate do not count it against spectate.
/// </summary>
public sealed class StreamHold
{
    public const string Phase = "StreamHold";

    /// <summary>How often the hold reads the stream again, and refreshes status.json.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often a hold that goes on logs that it still holds.</summary>
    public static readonly TimeSpan ReminderInterval = TimeSpan.FromMinutes(5);

    /// <summary>How often a hold asks for a new release, which may install meanwhile.</summary>
    public static readonly TimeSpan ReleaseCheckInterval = TimeSpan.FromMinutes(10);

    private readonly ILogger logger;
    private readonly AppSettings settings;
    private readonly IObsController obs;
    private readonly IConnectivityWatchdog connectivity;
    private readonly SpectatorStatusStore statusStore;
    private readonly Func<bool> armed;
    private readonly Func<bool> clientRunning;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Action<DateTimeOffset?, string> recordHold;

    public StreamHold(
        ILogger<StreamHold> logger,
        AppSettings settings,
        IObsController obs,
        IConnectivityWatchdog connectivity,
        SpectatorStatusStore statusStore
    )
        : this(
            logger,
            settings,
            obs,
            connectivity,
            statusStore,
            new ObsStreamArm().IsArmed,
            () => NamedProcess.IsRunning(NamedProcess.HeroesOfTheStorm)
        ) { }

    /// <param name="clientRunning">
    /// A Heroes client already runs: the report launched the next replay, so a match is on
    /// screen and is not held.
    /// </param>
    internal StreamHold(
        ILogger logger,
        AppSettings settings,
        IObsController obs,
        IConnectivityWatchdog connectivity,
        SpectatorStatusStore statusStore,
        Func<bool> armed,
        Func<bool> clientRunning,
        Func<DateTimeOffset> now = null,
        Func<TimeSpan, CancellationToken, Task> delay = null,
        Action<DateTimeOffset?, string> recordHold = null
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.obs = obs ?? throw new ArgumentNullException(nameof(obs));
        this.connectivity = connectivity ?? throw new ArgumentNullException(nameof(connectivity));
        this.statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        this.armed = armed ?? (() => false);
        this.clientRunning = clientRunning ?? (() => false);
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.delay = delay ?? Task.Delay;
        this.recordHold = recordHold ?? ServiceHeartbeat.RecordStreamHold;
    }

    /// <summary>
    /// Whether the next replay waits. OBS is read only when the stream is desired here, so a box
    /// that does not stream (the dev settings, a disarmed machine) never holds and is not asked.
    /// </summary>
    /// <param name="matchOver">
    /// The report of a finished match asks before it preloads the next replay: no match is on
    /// screen then, even while the last client is still closing.
    /// </param>
    public StreamHoldCheck Check(bool matchOver = false)
    {
        if (!(settings.Spectate?.HoldWhileStreamDown ?? true))
        {
            return StreamHoldCheck.Free("Spectate:HoldWhileStreamDown is false.");
        }

        if (!ObsDesired.StreamIsDesired(settings.OBS))
        {
            return StreamHoldCheck.Free(
                "The stream is not desired here (OBS:Enabled and OBS:StreamingEnabled)."
            );
        }

        if (!Safe(armed))
        {
            return StreamHoldCheck.Free("This machine is not armed for Twitch ingest.");
        }

        if (!connectivity.IsOnline)
        {
            return StreamHoldCheck.Free("Connectivity is down, so the outage mode applies.");
        }

        if (!matchOver && Safe(clientRunning))
        {
            return StreamHoldCheck.Free(
                "A Heroes client already runs the next replay; a match on screen is not held."
            );
        }

        ObsStreamHealth health;
        try
        {
            health = obs.CheckStreamHealth();
        }
        catch (Exception e)
        {
            health = ObsStreamHealth.Unknown(
                null,
                "OBS stream status was not read. " + e.Message,
                now()
            );
        }

        health ??= ObsStreamHealth.Unknown(null, "OBS stream status was not read.", now());
        return health.IsLive
            ? StreamHoldCheck.Free("The stream is live.", health)
            : new StreamHoldCheck(true, health, health.State + ". " + health.Detail);
    }

    /// <summary>
    /// Holds until the stream is live, the hold no longer applies, a release is staged
    /// (<paramref name="releaseStaged"/>, asked every <see cref="ReleaseCheckInterval"/>), or
    /// spectate stops. One warning when it starts, an information line every
    /// <see cref="ReminderInterval"/>, and one when it ends. There is no time limit.
    /// </summary>
    public async Task<StreamHoldEnd> HoldAsync(
        StreamHoldCheck first,
        Func<Task<bool>> releaseStaged,
        CancellationToken stop
    )
    {
        DateTimeOffset since = now();
        StreamHoldCheck check = first ?? Check();
        logger.LogWarning(
            "Holding the next replay: the stream is desired and not live ({State}). {Detail} Spectate shows the waiting scene and loads no replay until the stream is live again, with no time limit (Spectate:HoldWhileStreamDown). The stream reconcile keeps recovering it; Twitch requests stay queued.",
            check.Health?.State,
            check.Health?.Detail
        );
        bool waitingShown = ShowWaitingScene(check.Health);
        DateTimeOffset nextReminder = since + ReminderInterval;
        DateTimeOffset nextRelease = since + ReleaseCheckInterval;
        StreamHoldEnd end;
        string why;
        try
        {
            while (true)
            {
                Publish(since, check);
                if (!await Wait(stop).ConfigureAwait(false))
                {
                    end = StreamHoldEnd.Stopped;
                    why = "spectate is stopping";
                    break;
                }

                check = Check();
                if (!check.Holds)
                {
                    end =
                        check.Health?.IsLive == true
                            ? StreamHoldEnd.Live
                            : StreamHoldEnd.NotDesired;
                    why = check.Reason;
                    break;
                }

                waitingShown = waitingShown || ShowWaitingScene(check.Health);
                DateTimeOffset at = now();
                if (at >= nextReminder)
                {
                    nextReminder = at + ReminderInterval;
                    logger.LogInformation(
                        "Still holding the next replay after {Held}: the stream is {State}. {Detail}",
                        Round(at - since),
                        check.Health?.State,
                        check.Health?.Detail
                    );
                }

                if (releaseStaged != null && at >= nextRelease)
                {
                    nextRelease = at + ReleaseCheckInterval;
                    if (await Staged(releaseStaged).ConfigureAwait(false))
                    {
                        end = StreamHoldEnd.ReleaseStaged;
                        why = "a new release is staged and installs now";
                        break;
                    }
                }
            }
        }
        finally
        {
            Clear();
        }

        logger.LogInformation(
            "The hold of the next replay ends after {Held}: {Reason}",
            Round(now() - since),
            why
        );
        return end;
    }

    private void Publish(DateTimeOffset since, StreamHoldCheck check)
    {
        try
        {
            recordHold(since, check.Reason);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not put the stream hold in the heartbeat.");
        }

        // Every poll, so status.json stays fresh (a snapshot older than 15 s reads as stale).
        statusStore.Patch(status =>
        {
            status.SpectatorRunning = true;
            status.Phase = Phase;
            status.Timer = null;
            status.Focus = null;
            status.StreamHoldSince = since;
            status.StreamHoldReason = check.Reason;
        });
    }

    private void Clear()
    {
        try
        {
            recordHold(null, null);
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not clear the stream hold in the heartbeat.");
        }

        statusStore.Patch(status =>
        {
            status.StreamHoldSince = null;
            status.StreamHoldReason = null;
            if (string.Equals(status.Phase, Phase, StringComparison.Ordinal))
            {
                status.Phase = "Waiting";
            }
        });
    }

    /// <summary>The waiting scene, once OBS answers. False while it does not.</summary>
    private bool ShowWaitingScene(ObsStreamHealth health)
    {
        if (health == null || health.State == ObsStreamState.Unknown)
        {
            return false;
        }

        try
        {
            obs.SwapToWaitingScene();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not show the waiting scene during the stream hold.");
        }

        return true;
    }

    private async Task<bool> Wait(CancellationToken stop)
    {
        if (stop.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            await delay(PollInterval, stop).ConfigureAwait(false);
            return !stop.IsCancellationRequested;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> Staged(Func<Task<bool>> releaseStaged)
    {
        try
        {
            return await releaseStaged().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not check for a new release during the stream hold.");
            return false;
        }
    }

    private bool Safe(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "A stream hold condition could not be read.");
            return false;
        }
    }

    private static TimeSpan Round(TimeSpan span) =>
        TimeSpan.FromSeconds(Math.Round(Math.Max(0, span.TotalSeconds)));
}
