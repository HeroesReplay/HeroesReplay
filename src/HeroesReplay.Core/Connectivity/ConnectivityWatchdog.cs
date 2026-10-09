using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Status;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Connectivity;

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
    private readonly object gate = new();
    private int failCount;
    private int recoverCount;
    private volatile bool keepStreamThroughRestart;

    public ConnectivityWatchdog(
        ILogger<ConnectivityWatchdog> logger,
        AppSettings settings,
        INetworkProbe probe,
        SpectatorStatusStore statusStore,
        CancellationTokenProvider tokenProvider,
        IObsController obsController = null,
        IHeroesProfileResume heroesProfileResume = null,
        IReplayResume replayResume = null,
        Func<bool> gameIsRunning = null
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
        bool online = IsOnline;
        string detail = snapshot.Describe();
        // Written only when connectivity or an OBS field (the scene on air #282, the stream
        // block) differs from status.json. A scene switch the spectator already wrote (#357)
        // is not written again here. The OBS state is read under the store's lock, so an older
        // snapshot does not overwrite the scene the spectator just wrote.
        statusStore.PatchIfChanged(status =>
        {
            bool connectivityChanged =
                status.ConnectivityOnline != online
                || !string.Equals(status.Connectivity, detail, StringComparison.Ordinal);
            status.ConnectivityOnline = online;
            status.Connectivity = detail;
            return ObsStatus.Copy(status, obsController?.ReadObsState()) | connectivityChanged;
        });

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
            "Connectivity watchdog probing {Host} every {Interval}. The Heroes Profile website is not probed. TwitchWebsite={TwitchWebsite}. StreamingEnabled={StreamingEnabled}. NativeReconnectOwnsShortOutages={NativeReconnect}. StreamStuckAfter={StreamStuckAfter}.",
            Settings.InternetHost,
            ProbeInterval(),
            probeTwitch ? Settings.TwitchUri : "skipped",
            probeTwitch,
            true,
            settings.OBS?.StreamStuckAfter
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

    /// <summary>
    /// While online, a desired stream that is not live goes to the OBS coordinator's reconcile:
    /// an inactive output is started, and one stuck reconnecting or frozen past
    /// <c>OBS:StreamStuckAfter</c> is restarted (#395). An active output alone is not live. The
    /// coordinator logs what it did, at most one warning per attempt, so nothing is logged here.
    /// </summary>
    private void ReconcileDesiredStream()
    {
        if (obsController == null || !IsOnline || !IngestAllowed())
        {
            return;
        }

        try
        {
            if (obsController.ReadStreamHealth()?.IsLive == true)
            {
                return;
            }

            obsController.StartStreaming();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "OBS stream reconcile failed.");
        }
    }

    public void KeepStreamThroughRestart(bool keep) => keepStreamThroughRestart = keep;

    private void StopStreamForShutdown()
    {
        if (obsController == null || !IngestAllowed())
        {
            return;
        }

        if (keepStreamThroughRestart && KeepStreamOnWaitingScene())
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

    /// <summary>
    /// A release restart leaves the stream live on the waiting scene. The new install's reconcile
    /// finds it live and keeps it, so viewers see the waiting scene, not an offline channel.
    /// </summary>
    private bool KeepStreamOnWaitingScene()
    {
        string scene = settings.OBS?.WaitingSceneName;
        if (string.IsNullOrWhiteSpace(scene))
        {
            logger.LogInformation(
                "OBS:WaitingSceneName is not set, so the stream stops for the release restart."
            );
            return false;
        }

        try
        {
            obsController.SwapToWaitingScene();
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not show the waiting scene, so the stream stops for the release restart."
            );
            return false;
        }

        logger.LogInformation(
            "The OBS stream stays live on {Scene} while the new release installs and starts.",
            scene
        );
        return true;
    }

    private bool IngestAllowed() => SessionMedia.ShouldStream(settings.OBS);

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
