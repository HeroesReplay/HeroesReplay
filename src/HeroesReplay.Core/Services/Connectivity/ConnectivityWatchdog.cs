using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Shared;
using HeroesReplay.Core.Services.Status;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Connectivity;

public sealed class ConnectivityWatchdog : IConnectivityWatchdog
{
    private readonly ILogger<ConnectivityWatchdog> logger;
    private readonly AppSettings settings;
    private readonly INetworkProbe probe;
    private readonly SpectatorStatusStore statusStore;
    private readonly CancellationTokenProvider tokenProvider;
    private readonly IObsController obsController;
    private readonly IHeroesProfileResume heroesProfileResume;
    private readonly IReplayResume replayResume;
    private readonly Func<bool> gameIsRunning;
    private readonly Func<string> machineName;
    private readonly object gate = new();
    private int failCount;
    private int recoverCount;
    private string lastWrittenDetail;
    private bool? lastWrittenOnline;

    public ConnectivityWatchdog(
        ILogger<ConnectivityWatchdog> logger,
        AppSettings settings,
        INetworkProbe probe,
        SpectatorStatusStore statusStore,
        CancellationTokenProvider tokenProvider,
        IObsController obsController = null,
        IHeroesProfileResume heroesProfileResume = null,
        IReplayResume replayResume = null,
        Func<bool> gameIsRunning = null,
        Func<string> machineName = null
    )
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
        this.statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        this.obsController = obsController;
        this.heroesProfileResume = heroesProfileResume;
        this.replayResume = replayResume;
        this.gameIsRunning = gameIsRunning;
        this.machineName = machineName ?? (() => Environment.MachineName);
        IsOnline = true;
        Last = new ConnectivitySnapshot
        {
            At = DateTimeOffset.UtcNow,
            Internet = true,
            Twitch = true,
            HeroesProfile = true,
        };
    }

    public bool IsOnline { get; private set; }

    public TimeSpan DownFor
    {
        get
        {
            lock (gate)
            {
                if (IsOnline)
                {
                    return TimeSpan.Zero;
                }

                return TimeSpan.FromSeconds(15 * (long)failCount);
            }
        }
    }

    public ConnectivitySnapshot Last { get; private set; }
    public event EventHandler<ConnectivityChangedEventArgs> Changed;

    public async Task<ConnectivitySnapshot> ProbeAsync(CancellationToken cancellationToken)
    {
        bool internet = await probe.ProbeInternetAsync(cancellationToken).ConfigureAwait(false);
        bool probeTwitch = SessionMedia.ShouldStream(settings.OBS);
        bool twitch = false;
        if (probeTwitch)
        {
            twitch = await probe.ProbeTwitchAsync(cancellationToken).ConfigureAwait(false);
        }

        return new ConnectivitySnapshot
        {
            At = DateTimeOffset.UtcNow,
            Internet = internet,
            Twitch = twitch,
            TwitchProbed = probeTwitch,
            HeroesProfile = true,
        };
    }

    public bool Apply(ConnectivitySnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        ConnectivityChangedEventArgs changed = null;
        lock (gate)
        {
            Last = snapshot;
            if (snapshot.Internet)
            {
                failCount = 0;
                recoverCount++;
            }
            else
            {
                recoverCount = 0;
                failCount++;
            }

            int failThreshold = Math.Max(1, Settings.FailThreshold);
            int recoverThreshold = Math.Max(1, Settings.RecoverThreshold);

            if (IsOnline && failCount >= failThreshold)
            {
                IsOnline = false;
                changed = new ConnectivityChangedEventArgs
                {
                    IsOnline = false,
                    Snapshot = snapshot,
                };
            }
            else if (!IsOnline && recoverCount >= recoverThreshold)
            {
                IsOnline = true;
                changed = new ConnectivityChangedEventArgs { IsOnline = true, Snapshot = snapshot };
            }
        }

        ReconcileDesiredStream();
        string detail = snapshot.Describe();
        if (changed != null || lastWrittenOnline != IsOnline || lastWrittenDetail != detail)
        {
            lastWrittenOnline = IsOnline;
            lastWrittenDetail = detail;
            statusStore.Patch(status =>
            {
                status.ConnectivityOnline = IsOnline;
                status.Connectivity = detail;
                ObsStatus.Copy(status, obsController?.ReadObsState());
            });
        }

        if (changed == null)
        {
            return false;
        }

        ConnectivityResume.Decision decision = ConnectivityResume.Decide(
            wasOnline: !changed.IsOnline,
            isOnline: changed.IsOnline,
            streamingEnabled: SessionMedia.ShouldStream(settings.OBS)
        );

        logger.LogWarning(
            "Connectivity {State}: {Detail}",
            changed.IsOnline ? "restored" : "lost",
            snapshot.Describe()
        );
        if (decision.RetryHeroesProfile)
        {
            heroesProfileResume?.Arm();
            logger.LogInformation(
                "Connectivity restored. Retrying Heroes Profile list/download once."
            );
            NoteBattleNet();
            RequestReplayIfGameIsGone();
        }

        HandleStream(decision);
        Changed?.Invoke(this, changed);
        return true;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken == default)
        {
            cancellationToken = tokenProvider.Token;
        }

        if (!Settings.Enabled)
        {
            logger.LogInformation("Connectivity watchdog disabled.");
            return;
        }

        bool probeTwitch = SessionMedia.ShouldStream(settings.OBS);
        logger.LogInformation(
            "Connectivity watchdog probing {Host} every {Interval}. The Heroes Profile website is not probed. TwitchWebsite={TwitchWebsite}. StreamingEnabled={StreamingEnabled}. NativeReconnectOwnsShortOutages={NativeReconnect}.",
            Settings.InternetHost,
            ProbeInterval(),
            probeTwitch ? Settings.TwitchUri : "skipped",
            probeTwitch,
            true
        );

        try
        {
            ReconcileDesiredStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    ConnectivitySnapshot snapshot = await ProbeAsync(cancellationToken)
                        .ConfigureAwait(false);
                    Apply(snapshot);
                    await Task.Delay(ProbeInterval(), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Connectivity probe failed.");
                    await Task.Delay(ProbeInterval(), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
            {
                StopStreamForShutdown();
            }
        }
    }

    private TimeSpan ProbeInterval()
    {
        if (SessionMedia.ShouldStream(settings.OBS))
        {
            return Positive(Settings.Interval, TimeSpan.FromSeconds(15));
        }

        return Positive(Settings.IdleInterval, TimeSpan.FromMinutes(5));
    }

    private static TimeSpan Positive(TimeSpan value, TimeSpan fallback) =>
        value > TimeSpan.Zero ? value : fallback;

    private ConnectivitySettings Settings => settings.Connectivity ?? new ConnectivitySettings();

    private void NoteBattleNet()
    {
        bool running = NamedProcess.IsRunning(NamedProcess.BattleNet);
        if (running)
        {
            logger.LogInformation("Battle.net is running.");
            return;
        }

        logger.LogInformation(
            "Battle.net is not running. Login is not automated. The next replay launch opens Battle.net when the replay requires it."
        );
    }

    private void RequestReplayIfGameIsGone()
    {
        if (replayResume == null)
        {
            return;
        }

        TimeSpan downFor = DownFor;
        OperatingMode mode = OutageMode.Decide(IsOnline, downFor, TimeSpan.Zero);
        if (!OutageMode.MaySpectate(mode))
        {
            logger.LogInformation(
                "Spectate stays paused ({Mode}). The current replay is not marked played.",
                mode
            );
            return;
        }

        SpectatorStatus status = statusStore.Read();
        bool running =
            gameIsRunning != null
                ? gameIsRunning()
                : NamedProcess.IsRunning(NamedProcess.HeroesOfTheStorm);
        if (
            !ReplayResumeRules.ShouldReplay(
                running,
                status?.ReplayId,
                status?.CompletedReplayId,
                status?.ReplayPath
            )
        )
        {
            if (running)
            {
                logger.LogInformation(
                    "Connectivity restored. Heroes of the Storm is still running, so this replay stays on screen."
                );
            }

            return;
        }

        replayResume.Request(status.ReplayId.Value, status.ReplayPath);
        logger.LogInformation(
            "Connectivity restored. Heroes of the Storm is not running. Will replay {ReplayId}.",
            status.ReplayId
        );
    }

    private void HandleStream(ConnectivityResume.Decision decision)
    {
        if (obsController == null || decision.StartStream == decision.StopStream)
        {
            return;
        }

        if (!IngestAllowed())
        {
            return;
        }

        try
        {
            ObsStreamResult result = decision.StopStream
                ? obsController.StopStreaming()
                : obsController.StartStreaming();
            LogStream(result, decision.StopStream ? "stop" : "start");
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not {Action} OBS stream after connectivity change.",
                decision.StartStream ? "start" : "stop"
            );
        }
    }

    private void ReconcileDesiredStream()
    {
        if (obsController == null || !IsOnline || !IngestAllowed())
        {
            return;
        }

        try
        {
            if (obsController.IsStreaming())
            {
                return;
            }

            LogStream(obsController.StartStreaming(), "reconcile");
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "OBS stream reconcile failed.");
        }
    }

    private void StopStreamForShutdown()
    {
        if (obsController == null || !IngestAllowed())
        {
            return;
        }

        try
        {
            LogStream(obsController.StopStreaming(), "shutdown");
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not confirm the OBS stream inactive during shutdown.");
        }
    }

    private bool IngestAllowed() =>
        TwitchIngestGuard.Allows(Machine(), SessionMedia.ShouldStream(settings.OBS));

    private string Machine()
    {
        try
        {
            return machineName();
        }
        catch (Exception e)
        {
            logger.LogDebug(e, "Could not read the machine name. Twitch ingest stays off.");
            return null;
        }
    }

    private void LogStream(ObsStreamResult result, string action)
    {
        if (result == null || result.Succeeded || result.Failure == ObsOutputFailure.NotRequested)
        {
            return;
        }

        logger.LogWarning(
            "OBS stream {Action} was not confirmed ({Failure}). {Detail}",
            action,
            result.Failure,
            result.Detail
        );
    }
}
