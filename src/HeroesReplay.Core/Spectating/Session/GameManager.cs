using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.Spectating.Capture;
using HeroesReplay.Core.Spectating.Control;
using HeroesReplay.Core.Spectating.Screens;
using HeroesReplay.Core.Status;
using HeroesReplay.Core.Telemetry;
using HeroesReplay.Core.Twitch.Rewards;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Search;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Spectating.Session;

public class GameManager : IGameManager
{
    private readonly AppSettings settings;
    private readonly IReplayContextSetter contextSetter;
    private readonly ISpectator spectator;
    private readonly IGameController gameController;
    private readonly IObsController obsController;
    private readonly IReplayContext context;
    private readonly SpectatorStatusStore statusStore;
    private readonly StormClientConfigurator clientConfigurator;
    private int? clientPreparedFor;
    private readonly IYouTubeReplayLookup youTubeReplayLookup;
    private readonly RecordingClock recordingClock;
    private readonly ILogger<GameManager> logger;
    private readonly MediaPolicyAttemptLog mediaPolicy;
    private readonly IGameData gameData;
    private readonly CancellationTokenProvider tokenProvider;

    public GameManager(
        AppSettings settings,
        IReplayContextSetter contextSetter,
        ISpectator spectator,
        IGameController gameController,
        IObsController obsController,
        IReplayContext context,
        SpectatorStatusStore statusStore,
        StormClientConfigurator clientConfigurator,
        IYouTubeReplayLookup youTubeReplayLookup,
        RecordingClock recordingClock,
        ILogger<GameManager> logger,
        MediaPolicyAttemptLog mediaPolicy,
        IGameData gameData,
        CancellationTokenProvider tokenProvider
    )
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.contextSetter =
            contextSetter ?? throw new ArgumentNullException(nameof(contextSetter));
        this.spectator = spectator ?? throw new ArgumentNullException(nameof(spectator));
        this.gameController =
            gameController ?? throw new ArgumentNullException(nameof(gameController));
        this.obsController =
            obsController ?? throw new ArgumentNullException(nameof(obsController));
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        this.statusStore = statusStore ?? throw new ArgumentNullException(nameof(statusStore));
        this.clientConfigurator =
            clientConfigurator ?? throw new ArgumentNullException(nameof(clientConfigurator));
        this.youTubeReplayLookup =
            youTubeReplayLookup ?? throw new ArgumentNullException(nameof(youTubeReplayLookup));
        this.recordingClock =
            recordingClock ?? throw new ArgumentNullException(nameof(recordingClock));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.mediaPolicy = mediaPolicy ?? throw new ArgumentNullException(nameof(mediaPolicy));
        this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        this.tokenProvider =
            tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public async Task<ReplaySessionKind> LaunchAndSpectate(
        LoadedReplay loadedReplay,
        Action<ReplaySessionKind> outcomeKnown,
        Func<Task<LoadedReplay>> whileReporting
    )
    {
        using Activity replaySession = ReplaySessionFile.Open(loadedReplay?.ReplayId);
        if (replaySession != null && loadedReplay?.ReplayId is int openedReplayId)
        {
            logger.LogInformation(
                "Replay session {ReplayId} trace {TraceId}.",
                openedReplayId,
                replaySession.TraceId
            );
        }

        LinkRequest(loadedReplay);
        MediaRetention.SweepAndLog(settings, logger);
        await MarkExistingYouTubeVideoAsync(loadedReplay).ConfigureAwait(false);
        MediaPolicySnapshot preLaunch = await mediaPolicy
            .RecordPreLaunchAsync(
                loadedReplay,
                settings.ReplayMedia,
                CancellationToken.None,
                gameData.Heroes
            )
            .ConfigureAwait(false);
        ApplyPreLaunchPolicy(loadedReplay, preLaunch);
        CapRecordingToPublication(loadedReplay, preLaunch);
        await contextSetter.SetContextAsync(loadedReplay);
        bool obsSession = false;
        bool enteredMatch = false;
        statusStore.Patch(status => ShowLoading(status, loadedReplay, context.Current));
        // The launch and loading phase starts. Match progress, the report, or the end of the
        // session ends it; a launch with no progress for too long is a stalled spectate (#249).
        ServiceHeartbeat.RecordLaunching();

        try
        {
            SkipRecordingUnderDiskPressure(loadedReplay);
            using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.spectate");
            HeroesReplayTelemetry.TagReplay(
                activity,
                loadedReplay?.FileInfo?.FullName,
                loadedReplay?.Replay?.Map,
                loadedReplay?.ReplayId,
                loadedReplay?.Replay?.ReplayVersion
            );

            EnsureWindowedClient(loadedReplay?.ReplayId);
            recordingClock.Reset();
            ClientHoldReason hold = await gameController.LaunchAsync().ConfigureAwait(false);
            if (hold == ClientHoldReason.AwardScreen)
            {
                logger.LogInformation(
                    "The client is on the award screen. Replay {ReplayId} is over. Report scenes start and the next replay loads.",
                    loadedReplay?.ReplayId
                );
                spectator.RecordHold(hold);
                if (settings.OBS.Enabled)
                {
                    obsController.BeginSession();
                    obsSession = true;
                    statusStore.Patch(status => status.ObsSession = true);
                    obsController.ConfigureFromContext();
                }

                enteredMatch = true;
            }
            else if (hold == ClientHoldReason.None)
            {
                RememberInterfaceBuild();
                if (settings.OBS.Enabled)
                {
                    obsController.BeginSession();
                    obsSession = true;
                    statusStore.Patch(status => status.ObsSession = true);
                    obsController.ConfigureFromContext();
                    await StartRecordingWhenMatchIsVisible(loadedReplay).ConfigureAwait(false);
                }

                enteredMatch = true;
                await spectator.SpectateAsync().ConfigureAwait(false);
                activity?.SetTag("session.outcome", spectator.Outcome.ToString());
            }
            else if (hold != ClientHoldReason.None)
            {
                spectator.RecordHold(hold);
                await RecordPublicationAsync(loadedReplay, recording: null).ConfigureAwait(false);
                activity?.SetTag("session.outcome", spectator.Outcome.ToString());
                if (hold == ClientHoldReason.BuildNotInstalled)
                {
                    logger.LogWarning(
                        "Replay {ReplayId} needs a Heroes build that is not installed. It stays queued. The current patch was not launched.",
                        loadedReplay?.ReplayId
                    );
                }
                else if (hold == ClientHoldReason.ClientNotReady)
                {
                    logger.LogWarning(
                        "Heroes is still preparing. Replay {ReplayId} stays queued. The client stays open.",
                        loadedReplay?.ReplayId
                    );
                }
                else
                {
                    logger.LogWarning(
                        "Heroes is on the {Hold} dialog. Replay {ReplayId} stays queued. The client stays open and OBS uses the waiting scene.",
                        hold,
                        loadedReplay?.ReplayId
                    );
                }
                statusStore.Patch(status =>
                {
                    status.SpectatorRunning = true;
                    status.Phase = "Waiting";
                    status.Timer = null;
                    status.Outcome = spectator.Outcome.ToString();
                });
                if (hold == ClientHoldReason.BuildNotInstalled)
                {
                    logger.LogInformation(
                        "Replay {ReplayId} stays queued. The waiting scene was not used.",
                        loadedReplay?.ReplayId
                    );
                }
                else
                {
                    ParkWaitingScene();
                }
                return DecideOutcome(loadedReplay, outcomeKnown);
            }
        }
        finally
        {
            ObsRecordingResult stopped = null;
            if (enteredMatch && obsSession)
            {
                try
                {
                    stopped = obsController.StopRecording();
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Could not stop OBS recording.");
                }

                bool allowsMedia = MatchCompletion.AllowsMedia(
                    spectator.Outcome,
                    recordingClock.SampleCount,
                    recordingClock.Elapsed
                );
                if (RecordingOwnership.CanPublish(stopped, allowsMedia))
                {
                    try
                    {
                        await MatchClipExporter
                            .ExportAsync(
                                context.Current?.LoadedReplay?.Replay,
                                context.Current?.LoadedReplay?.ReplayId,
                                context.Current?.Directory?.FullName,
                                stopped.OutputPath,
                                recordingClock,
                                settings.YouTube,
                                settings.YouTube?.EntryFileName,
                                logger,
                                gameData.Heroes
                            )
                            .ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Could not cut team-kill clips.");
                    }
                }
                else
                {
                    await UnpublishRecordingAsync(loadedReplay, stopped, allowsMedia)
                        .ConfigureAwait(false);
                }
            }

            if (enteredMatch)
            {
                await RecordPublicationAsync(loadedReplay, stopped).ConfigureAwait(false);
                ReplayShutdown.CaptureEndThenKill(gameController, logger);
            }
        }

        return await FinishSessionAsync(
                loadedReplay,
                enteredMatch,
                obsSession,
                outcomeKnown,
                whileReporting
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The recording is stopped, its publication is decided, and Heroes is closed. The outcome
    /// is heard first, then the report scenes run and the next replay launches behind them.
    /// </summary>
    internal async Task<ReplaySessionKind> FinishSessionAsync(
        LoadedReplay loadedReplay,
        bool enteredMatch,
        bool obsSession,
        Action<ReplaySessionKind> outcomeKnown,
        Func<Task<LoadedReplay>> whileReporting
    )
    {
        ReplaySessionKind kind = DecideOutcome(loadedReplay, outcomeKnown);
        // The report and the next replay's preload are their own bounded phase, not a launch.
        ServiceHeartbeat.RecordLaunchEnded();
        await ReportAndHandOffAsync(enteredMatch, obsSession, whileReporting).ConfigureAwait(false);
        return kind;
    }

    /// <summary>
    /// The outcome is final here. The redemption and the caller hear it now, before the report
    /// scenes and the next replay's launch, so a stop or a kill during the report cannot lose it.
    /// </summary>
    private ReplaySessionKind DecideOutcome(
        LoadedReplay loadedReplay,
        Action<ReplaySessionKind> outcomeKnown
    )
    {
        ReplaySessionKind kind = ReplaySession.Classify(spectator.Outcome);
        RecordRedemption(loadedReplay, spectator.Outcome);
        try
        {
            outcomeKnown?.Invoke(kind);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not record the {Session} outcome of replay {ReplayId}.",
                kind,
                loadedReplay?.ReplayId
            );
        }

        return kind;
    }

    /// <summary>
    /// The report scenes and the next replay's launch, then this session's OBS connection ends.
    /// A stop skips the report or cuts it short, and the next replay is not launched.
    /// </summary>
    private async Task ReportAndHandOffAsync(
        bool enteredMatch,
        bool obsSession,
        Func<Task<LoadedReplay>> whileReporting
    )
    {
        bool reports =
            enteredMatch
            && obsSession
            && (
                spectator.Outcome == MatchOutcome.VerifiedCompleted
                || spectator.Outcome == MatchOutcome.AwardScreen
            );
        if (reports && tokenProvider.Token.IsCancellationRequested)
        {
            logger.LogInformation(
                "Spectate is stopping. The report scenes are skipped and the next replay is not launched."
            );
            reports = false;
        }

        try
        {
            // A verified match or an award screen preloads the next replay and runs the report scenes.
            if (reports)
            {
                Task<LoadedReplay> nextLoad = InvokeNextLoad(whileReporting);
                using var cutReport = CancellationTokenSource.CreateLinkedTokenSource(
                    tokenProvider.Token
                );
                Task report = obsController.CycleReportAsync(cutReport.Token);
                Task<NextMatchLaunch> launch = LaunchNextDuringReportAsync(
                    nextLoad,
                    report,
                    cutReport
                );
                await Task.WhenAll(report, launch).ConfigureAwait(false);
                NextMatchLaunch nextMatch = await launch.ConfigureAwait(false);
                if (ReplayLoadCue.SelectsGameScene(nextMatch))
                {
                    try
                    {
                        if (cutReport.IsCancellationRequested)
                        {
                            logger.LogInformation(
                                "The next map is loading or its clock is visible. Switching to the game scene so spectating starts now."
                            );
                        }
                        else
                        {
                            logger.LogInformation(
                                "Report scenes finished and the next match is loaded. Switching to the game scene."
                            );
                        }

                        obsController.SwapToGameScene();
                    }
                    catch (Exception e)
                    {
                        logger.LogWarning(e, "Could not switch OBS to the game scene.");
                    }
                }
                else if (ReplayLoadCue.SelectsWaitingScene(nextMatch))
                {
                    obsController.SwapToWaitingScene();
                }
                else
                {
                    logger.LogInformation(
                        "Report scenes finished. The next game is open without a loading screen or match clock, so the report scene stays."
                    );
                }
            }
        }
        finally
        {
            if (obsSession)
            {
                obsController.EndSession();
            }
        }
    }

    public MatchOutcome LastOutcome => spectator.Outcome;

    /// <summary>
    /// The status the Twitch process reads while the replay loads. A viewer-entered replay id
    /// turns predictions off for the whole session (#166).
    /// </summary>
    internal static void ShowLoading(
        SpectatorStatus status,
        LoadedReplay loadedReplay,
        ContextData current
    )
    {
        status.SpectatorRunning = true;
        status.Phase = "Loading";
        status.Map = EnglishMapNames.Prefer(
            loadedReplay?.HeroesProfileReplay?.Map,
            loadedReplay?.Replay?.Map,
            loadedReplay?.Replay?.MapAlternativeName
        );
        status.ReplayPath = loadedReplay?.FileInfo?.FullName;
        status.ReplayVersion = loadedReplay?.Replay?.ReplayVersion;
        status.ReplayId = loadedReplay?.ReplayId;
        status.Outcome = null;
        status.SuppressPredictions = ReplayRequestKind.ViewerEnteredReplayId(loadedReplay);
        status.GatesOpen = current?.GatesOpen.ToString();
        status.CoreKilled = current?.CoreKilled.ToString();
    }

    /// <summary>
    /// Every path that hands a replay to the session (the cache, the report preload, a requeue,
    /// a connectivity resume) is linked to its request here, by replay id (#165).
    /// </summary>
    private void LinkRequest(LoadedReplay loadedReplay)
    {
        string requests =
            string.IsNullOrWhiteSpace(settings.Location?.DataDirectory)
            || string.IsNullOrWhiteSpace(settings.HeroesProfileApi?.RequestsCacheDirectoryName)
                ? null
                : settings.RequestedReplayCachePath;
        if (CachedRequestReward.Attach(loadedReplay, requests))
        {
            logger.LogInformation(
                "Replay {ReplayId} is the request of {Login} ({Reward}, redemption {RedemptionId}).",
                loadedReplay.ReplayId,
                loadedReplay.RewardQueueItem.Request.Login,
                loadedReplay.RewardQueueItem.Request.RewardTitle,
                loadedReplay.RewardQueueItem.Request.RedemptionId
            );
        }
    }

    public bool LastMatchClockSeen => spectator.MatchClockSeen;

    public void ReleaseClientAfterDefer()
    {
        logger.LogWarning(
            "Closing Heroes so the next replay can start. Battle.net was not clicked."
        );
        gameController.Kill();
    }

    private void SkipRecordingUnderDiskPressure(LoadedReplay loadedReplay)
    {
        DiskBacklogDecision decision = SpectateAdmission.Evaluate(MeasureDisk(), settings.Disk);
        if (SpectateAdmission.MayRecord(decision))
        {
            if (decision.Pressure == DiskPressure.Warning)
            {
                logger.LogWarning(
                    "Disk is in warning ({Reason}). Spectate and recording continue.",
                    decision.Reason
                );
            }

            return;
        }

        if (loadedReplay != null)
        {
            loadedReplay.PolicyAllowsRecording = false;
        }

        logger.LogWarning(
            "Disk {Reason}. Replay {ReplayId} is spectated without a recording. Recordings already on disk stay.",
            decision.Reason,
            loadedReplay?.ReplayId
        );
    }

    private DiskBacklogInput MeasureDisk()
    {
        long free = long.MaxValue;
        try
        {
            string root = Path.GetPathRoot(settings.Location?.DataDirectory);
            if (!string.IsNullOrWhiteSpace(root))
            {
                free = new DriveInfo(root).AvailableFreeSpace;
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not read free disk space. Spectate continues.");
        }

        long pending = 0;
        try
        {
            pending = PendingUploadSize.Bytes(
                settings.ContextsDirectory,
                settings.YouTube?.EntryFileName,
                settings.YouTube?.EntryFileNameUploaded
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not measure pending uploads. Spectate continues.");
        }

        return new DiskBacklogInput { FreeBytes = free, PendingUploadBytes = pending };
    }

    /// <summary>
    /// A verified match is written for twitch connect, which marks the redemption FULFILLED.
    /// Any other outcome leaves it UNFULFILLED: the replay stays queued and plays again (#169).
    /// </summary>
    private void RecordRedemption(LoadedReplay loaded, MatchOutcome outcome)
    {
        RewardRequest request = loaded?.RewardQueueItem?.Request;
        Guid redemptionId = request?.RedemptionId ?? Guid.Empty;
        RedemptionEnd end = RedemptionDisposition.Decide(
            redemptionId != Guid.Empty,
            outcome == MatchOutcome.VerifiedCompleted
        );
        if (end == RedemptionEnd.None)
        {
            if (redemptionId != Guid.Empty)
            {
                logger.LogInformation(
                    "Redemption {RedemptionId} for replay {ReplayId} stays UNFULFILLED ({Outcome}). The replay stays queued.",
                    redemptionId,
                    loaded.ReplayId,
                    outcome
                );
            }

            return;
        }

        if (settings.Location?.DataDirectory == null)
        {
            return;
        }

        string path = Path.Combine(
            settings.Location.DataDirectory,
            RedemptionDispositionLog.FileName
        );
        try
        {
            RedemptionDispositionLog.Append(path, loaded.ReplayId, request, end);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                e,
                "Could not record redemption {RedemptionId} for replay {ReplayId}.",
                redemptionId,
                loaded.ReplayId
            );
            return;
        }

        logger.LogInformation(
            "Redemption {RedemptionId} for replay {ReplayId} is {Status}. twitch connect sends it to Twitch.",
            redemptionId,
            loaded.ReplayId,
            RewardRedemptionStatus.Decide(RewardRedemptionStatus.FromOutcome(outcome))
        );
    }

    private void ParkWaitingScene()
    {
        if (!settings.OBS.Enabled)
        {
            return;
        }

        try
        {
            obsController.SwapToWaitingScene();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not switch OBS to the waiting scene.");
        }
    }

    private Task<LoadedReplay> InvokeNextLoad(Func<Task<LoadedReplay>> whileReporting)
    {
        if (whileReporting == null)
        {
            return Task.FromResult<LoadedReplay>(null);
        }

        try
        {
            return whileReporting() ?? Task.FromResult<LoadedReplay>(null);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not start loading the next replay.");
            return Task.FromResult<LoadedReplay>(null);
        }
    }

    private async Task<NextMatchLaunch> LaunchNextDuringReportAsync(
        Task<LoadedReplay> nextLoad,
        Task report,
        CancellationTokenSource cutReport
    )
    {
        if (settings.Capture?.Method == CaptureMethod.None)
        {
            return NextMatchLaunch.NotStarted;
        }

        LoadedReplay next;
        try
        {
            next = await nextLoad.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load the next replay during the report.");
            return NextMatchLaunch.NotStarted;
        }

        if (next?.FileInfo == null || !next.FileInfo.Exists)
        {
            return NextMatchLaunch.NotStarted;
        }

        // services stop gives spectate 20 s to exit. Every wait below ends on the stop request,
        // so a stop during the report never waits out the hold or launches the next replay.
        CancellationToken stop = tokenProvider.Token;
        DateTimeOffset exitBy = DateTimeOffset.UtcNow.AddSeconds(20);
        while (gameController.IsGameRunning() && DateTimeOffset.UtcNow < exitBy)
        {
            if (
                !await WaitUnlessStoppingAsync(TimeSpan.FromMilliseconds(500), stop)
                    .ConfigureAwait(false)
            )
            {
                return HandOffStopped(next);
            }
        }

        if (gameController.IsGameRunning())
        {
            logger.LogWarning("The previous game is still running. The next replay stays queued.");
            return NextMatchLaunch.NotStarted;
        }

        logger.LogInformation(
            "Waiting {Seconds:0}s for Battle.net to finish closing the previous Heroes session.",
            ClientRelaunch.SettleAfterExit.TotalSeconds
        );
        if (
            !await WaitUnlessStoppingAsync(ClientRelaunch.SettleAfterExit, stop)
                .ConfigureAwait(false)
        )
        {
            return HandOffStopped(next);
        }

        TimeSpan beforeNext = NextReplayHold.Duration(
            settings.OBS?.BeforeNextReplay ?? NextReplayHold.Default
        );
        if (!await HoldBeforeNextLaunchAsync(report, beforeNext, stop).ConfigureAwait(false))
        {
            return HandOffStopped(next);
        }

        // Heroes is closed here, so Variables.txt can be repaired. The next session starts with
        // this client already running and could only warn (#206).
        EnsureWindowedClient(next.ReplayId);
        clientPreparedFor = next.ReplayId;

        try
        {
            await gameController
                .StartAuthenticatedReplayAsync(next.FileInfo.FullName, next.Replay?.ReplayVersion)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not start the next replay during the report.");
            return NextMatchLaunch.NotStarted;
        }

        DateTimeOffset seenBy = DateTimeOffset.UtcNow.AddMinutes(2);
        while (!gameController.IsGameRunning() && DateTimeOffset.UtcNow < seenBy)
        {
            if (
                !await WaitUnlessStoppingAsync(TimeSpan.FromMilliseconds(500), stop)
                    .ConfigureAwait(false)
            )
            {
                return HandOffStopped(next);
            }
        }

        if (!gameController.IsGameRunning())
        {
            logger.LogWarning(
                "Next replay {ReplayId} did not open during the report.",
                next.ReplayId
            );
            return NextMatchLaunch.NotStarted;
        }

        bool loggedReadFailure = false;
        bool reopenedFromHome = false;
        DateTimeOffset reopenAt = DateTimeOffset.UtcNow.AddSeconds(25);
        DateTimeOffset readyBy = DateTimeOffset.UtcNow.AddMinutes(3);
        while (DateTimeOffset.UtcNow < readyBy)
        {
            if (!reopenedFromHome && DateTimeOffset.UtcNow >= reopenAt)
            {
                try
                {
                    reopenedFromHome = await gameController
                        .OpenReplayFromHomeScreenAsync(next.FileInfo.FullName)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Could not open the next replay from the home screen.");
                }
            }

            if (!gameController.IsGameRunning())
            {
                logger.LogWarning(
                    "Next replay {ReplayId} closed before the loading screen or the match clock.",
                    next.ReplayId
                );
                return NextMatchLaunch.NotStarted;
            }

            TimeSpan? matchClock = await gameController
                .TryReadRunningMatchClockAsync()
                .ConfigureAwait(false);
            if (ReportHandoff.ShouldCutReport(mapLoading: false, matchClock))
            {
                logger.LogInformation(
                    "Next replay {ReplayId} match clock is {Clock}. The report stops so OBS shows the game through the countdown.",
                    next.ReplayId,
                    matchClock
                );
                cutReport.Cancel();
                return NextMatchLaunch.Presented;
            }

            bool mapLoading = false;
            try
            {
                mapLoading = await gameController
                    .IsReplayPresentedAsync(next)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                if (!loggedReadFailure)
                {
                    loggedReadFailure = true;
                    logger.LogWarning(
                        e,
                        "Could not read the next match screen for replay {ReplayId}.",
                        next.ReplayId
                    );
                }
            }

            if (ReportHandoff.ShouldCutReport(mapLoading, matchClock))
            {
                logger.LogInformation(
                    "Next replay {ReplayId} is on the map loading screen, in a match, or its match clock is running. The report stops so OBS shows the game.",
                    next.ReplayId
                );
                cutReport.Cancel();
                return NextMatchLaunch.Presented;
            }

            if (NextReplayHold.StopWhenReportEnds(beforeNext) && report.IsCompleted)
            {
                break;
            }

            if (!await WaitUnlessStoppingAsync(TimeSpan.FromSeconds(1), stop).ConfigureAwait(false))
            {
                return HandOffStopped(next);
            }
        }

        if (!gameController.IsGameRunning())
        {
            return NextMatchLaunch.NotStarted;
        }

        logger.LogWarning(
            "Next replay {ReplayId} is open, but no loading screen, match, or match clock was seen. The report scene stays, and its session checks the client again: a replay that is already playing starts there.",
            next.ReplayId
        );
        return NextMatchLaunch.ProcessOnly;
    }

    /// <summary>
    /// True when the hold and the report cycle are done and the next replay may launch. False as
    /// soon as spectate is stopping: the hold ends at once and the next replay is not launched.
    /// </summary>
    internal async Task<bool> HoldBeforeNextLaunchAsync(
        Task report,
        TimeSpan hold,
        CancellationToken stop
    )
    {
        if (hold <= TimeSpan.Zero)
        {
            return !stop.IsCancellationRequested;
        }

        logger.LogInformation(
            "Waiting {Hold} before launching the next replay so match-report, prediction-report, and request-queue can finish.",
            hold
        );
        Task pause = Task.Delay(hold, stop);
        Task finished = await Task.WhenAny(pause, report).ConfigureAwait(false);
        if (pause.IsCanceled)
        {
            return false;
        }

        if (ReferenceEquals(finished, report))
        {
            await ObserveReportAsync(report).ConfigureAwait(false);
            ShowWaitingSceneBeforeNextLaunch();
            return await RanOutAsync(pause).ConfigureAwait(false);
        }

        if (!report.IsCompleted)
        {
            logger.LogInformation(
                "The pause elapsed and a report scene is still on screen. The next replay waits until that cycle finishes."
            );
            await ObserveReportAsync(report).ConfigureAwait(false);
        }

        ShowWaitingSceneBeforeNextLaunch();
        return !stop.IsCancellationRequested;
    }

    /// <summary>True when <paramref name="delay"/> ran out. False as soon as spectate is stopping.</summary>
    private static Task<bool> WaitUnlessStoppingAsync(TimeSpan delay, CancellationToken stop) =>
        RanOutAsync(Task.Delay(delay, stop));

    private static async Task<bool> RanOutAsync(Task pause)
    {
        try
        {
            await pause.ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private NextMatchLaunch HandOffStopped(LoadedReplay next)
    {
        logger.LogInformation(
            "Spectate is stopping. The handoff to next replay {ReplayId} ends here, and that replay stays queued for the next start.",
            next?.ReplayId
        );
        return NextMatchLaunch.NotStarted;
    }

    private async Task ObserveReportAsync(Task report)
    {
        try
        {
            await report.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Report scenes ended before the next replay launched.");
        }
    }

    private void ShowWaitingSceneBeforeNextLaunch()
    {
        if (!settings.OBS.Enabled || string.IsNullOrWhiteSpace(settings.OBS.WaitingSceneName))
        {
            return;
        }

        try
        {
            obsController.SwapToWaitingScene();
            logger.LogInformation(
                "Selected {Scene} until the next match reaches the loading screen.",
                settings.OBS.WaitingSceneName
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not switch OBS to the waiting scene before the next replay."
            );
        }
    }

    private static void ApplyPreLaunchPolicy(LoadedReplay loaded, MediaPolicySnapshot snapshot)
    {
        if (loaded == null)
        {
            return;
        }

        loaded.PolicyAllowsRecording = snapshot?.AllowsRecording == true;
        loaded.PolicyAllowsPublication = false;
    }

    /// <summary>
    /// The media policy chose to record. The cap skips the recording when the upload and
    /// publication pipeline already holds what it can publish (#250). A request always records.
    /// </summary>
    private void CapRecordingToPublication(LoadedReplay loaded, MediaPolicySnapshot snapshot)
    {
        if (loaded?.PolicyAllowsRecording != true || snapshot?.Decision == null)
        {
            return;
        }

        RecordingCapDecision cap;
        try
        {
            cap = RecordingCap.Decide(
                RecordingCap.Measure(settings, snapshot.Decision.Priority, DateTimeOffset.UtcNow),
                settings.ReplayMedia
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                e,
                "Could not measure the upload backlog for replay {ReplayId}. It is recorded.",
                loaded.ReplayId
            );
            return;
        }

        if (cap.Allow)
        {
            logger.LogInformation(
                "Replay {ReplayId} recording cap allows it ({CapReason}): {InFlight} waiting for upload or publish time, capacity {Capacity}.",
                loaded.ReplayId,
                cap.Reason,
                cap.InFlight,
                cap.Capacity
            );
            return;
        }

        loaded.PolicyAllowsRecording = false;
        logger.LogInformation(
            "Replay {ReplayId} is spectated without a recording ({CapReason}): {InFlight} recording(s) already wait for upload or publish time, capacity {Capacity}.",
            loaded.ReplayId,
            cap.Reason,
            cap.InFlight,
            cap.Capacity
        );
    }

    private async Task RecordPublicationAsync(
        LoadedReplay loadedReplay,
        ObsRecordingResult recording
    )
    {
        string withheld = null;
        MediaPolicySnapshot snapshot = await mediaPolicy
            .RecordPublicationAsync(
                loadedReplay,
                new MediaPublicationFacts
                {
                    Outcome = spectator.Outcome,
                    MatchClockSeen = spectator.MatchClockSeen,
                    HudSamples = recordingClock.SampleCount,
                    RecordedFor = recordingClock.Elapsed,
                    Recording = recording,
                    AlreadyPublished = loadedReplay?.AlreadyOnYouTube == true,
                },
                CancellationToken.None
            )
            .ConfigureAwait(false);
        if (loadedReplay != null)
        {
            int publishedInWindow = PublishedThisDay(settings);
            PublicationAdmitResult admit = PublicationAdmit.Decide(
                snapshot?.Decision,
                publishedInWindow,
                settings?.ReplayMedia
            );
            loadedReplay.PolicyAllowsPublication = admit.Allow;
            withheld = admit.Allow ? null : admit.Reason;
            logger.LogInformation(
                "Replay {ReplayId} publication admit {Admit} reason {PublicationReason} published in window {PublishedInWindow}.",
                loadedReplay.ReplayId,
                admit.Allow,
                admit.Reason,
                publishedInWindow
            );
        }

        string directory = context.Current?.Directory?.FullName;
        bool wrote = await YouTubeEntryWriter
            .WriteIfAllowedAsync(
                directory,
                settings.YouTube?.EntryFileName,
                loadedReplay,
                settings.YouTube,
                isCompleteRecording: true,
                CancellationToken.None,
                gameData.Heroes
            )
            .ConfigureAwait(false);
        if (wrote)
        {
            logger.LogInformation(
                "Wrote the YouTube entry for replay {ReplayId} after the publication decision.",
                loadedReplay?.ReplayId
            );
            return;
        }

        await DiscardUnpublishedRecordingAsync(loadedReplay, recording, withheld)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A finished recording whose publication was withheld gets no <c>youtube-entry.json</c>,
    /// so it can never upload. It is deleted now, with the reason, instead of waiting for
    /// retention to remove it as never uploaded (#250). Without YouTube the file is left to
    /// retention as before.
    /// </summary>
    private async Task DiscardUnpublishedRecordingAsync(
        LoadedReplay loadedReplay,
        ObsRecordingResult recording,
        string withheld
    )
    {
        if (
            settings.YouTube?.Enabled != true
            || !RecordingOwnership.CanPublish(recording, allowsMedia: true)
            || !File.Exists(recording.OutputPath)
        )
        {
            return;
        }

        bool deleted = await RecordingDiscard
            .DeleteAsync(recording.OutputPath, logger)
            .ConfigureAwait(false);
        logger.LogWarning(
            "Replay {ReplayId} recording {Path} is not published ({Reason}), so no YouTube entry was written. {Action}",
            loadedReplay?.ReplayId,
            recording.OutputPath,
            withheld ?? "entry-not-written",
            deleted ? "The recording was deleted." : "Retention removes it later."
        );
    }

    /// <summary>
    /// Videos whose publish time falls in the last 24 hours. Every video the uploader sent has a
    /// slot at its publish time; the uploader's older ledger only held inserts that came back
    /// public, which a scheduled upload never does (#250).
    /// </summary>
    private static int PublishedThisDay(AppSettings settings) =>
        PublicationReservation.CountIn(
            PublicationReservation.PathFor(settings?.Location?.DataDirectory),
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(24)
        );

    private async Task MarkExistingYouTubeVideoAsync(LoadedReplay loadedReplay)
    {
        if (loadedReplay == null)
        {
            return;
        }

        try
        {
            loadedReplay.AlreadyOnYouTube = await youTubeReplayLookup
                .AlreadyUploadedAsync(loadedReplay, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not check YouTube for replay {ReplayId}. Recording stays on.",
                loadedReplay.ReplayId
            );
        }

        if (loadedReplay.AlreadyOnYouTube)
        {
            logger.LogInformation(
                "Replay {ReplayId} already has a YouTube video. This spectate will not record.",
                loadedReplay.HeroesProfileReplay?.Id ?? loadedReplay.ReplayId
            );
        }
    }

    private async Task StartRecordingWhenMatchIsVisible(LoadedReplay loadedReplay)
    {
        if (!SessionMedia.ShouldRecord(settings.OBS, loadedReplay))
        {
            return;
        }

        bool presented = false;
        try
        {
            presented = await gameController
                .IsReplayPresentedAsync(loadedReplay)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e,
                "Could not read the match screen before recording replay {ReplayId}.",
                loadedReplay?.ReplayId
            );
        }

        if (!MatchRecording.ShouldStart(recordingClock.IsRunning, presented))
        {
            logger.LogInformation(
                "OBS recording for replay {ReplayId} waits until the loading screen is visible or the match clock is running.",
                loadedReplay?.ReplayId
            );
            return;
        }

        ObsRecordingResult started = obsController.StartRecording();
        statusStore.Patch(status => ObsStatus.CopyRecording(status, started));
        if (!started.Owned)
        {
            logger.LogWarning(
                "OBS recording for replay {ReplayId} did not start ({Failure}). {Detail}",
                loadedReplay?.ReplayId,
                started.Failure,
                started.Detail
            );
            return;
        }

        recordingClock.Start();
    }

    private async Task UnpublishRecordingAsync(
        LoadedReplay loadedReplay,
        ObsRecordingResult stopped,
        bool allowsMedia
    )
    {
        string directory = context.Current?.Directory?.FullName;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        string uploadedName = settings.YouTube?.EntryFileNameUploaded;
        if (
            !string.IsNullOrWhiteSpace(uploadedName)
            && File.Exists(Path.Combine(directory, uploadedName))
        )
        {
            return;
        }

        string entryName = string.IsNullOrWhiteSpace(settings.YouTube?.EntryFileName)
            ? "youtube-entry.json"
            : settings.YouTube.EntryFileName;
        bool removedEntry = TryDelete(Path.Combine(directory, entryName));
        bool removedFile = await RecordingDiscard
            .DeleteAsync(RecordingOwnership.FileToDiscard(stopped, allowsMedia), logger)
            .ConfigureAwait(false);
        if (
            !removedEntry
            && !removedFile
            && !recordingClock.IsRunning
            && !RecordingWasAttempted(stopped)
        )
        {
            return;
        }

        logger.LogWarning(
            "Replay {ReplayId} recording is not published ({Outcome}, {Failure}, owned {Owned}, finalized {Finalized}, {Samples} clock samples over {Elapsed}). It was not sent to YouTube.",
            loadedReplay?.ReplayId,
            spectator.Outcome,
            stopped?.Failure,
            stopped?.Owned ?? false,
            stopped?.Finalized ?? false,
            recordingClock.SampleCount,
            recordingClock.Elapsed
        );
    }

    private static bool RecordingWasAttempted(ObsRecordingResult stopped) =>
        stopped != null
        && stopped.Failure != ObsOutputFailure.None
        && stopped.Failure != ObsOutputFailure.NotOwned
        && stopped.Failure != ObsOutputFailure.NotRequested;

    private bool TryDelete(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not delete {Path}.", path);
            return false;
        }
    }

    /// <summary>
    /// Writes windowed 1080p, background audio, and AhliObs into the root and account
    /// Variables.txt while Heroes is closed. A running client is never touched.
    /// </summary>
    private void EnsureWindowedClient(int? replayId)
    {
        try
        {
            ApplyClientPreset(replayId);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                e,
                "Could not write the Heroes client settings before replay {ReplayId}.",
                replayId
            );
        }
    }

    private void ApplyClientPreset(int? replayId)
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.client.configure");
        ClientStatusResult status = clientConfigurator.GetStatus();
        string newest = ClientInterfacePlan.Newest(
            InstalledClientCatalog.FileVersions(settings.Location?.GameInstallDirectory)
        );
        bool buildSealed = ClientInterfacePlan.IsSealed(
            ClientInterfaceSeal.Read(
                ClientInterfaceSeal.FilePath(settings.Location?.DataDirectory)
            ),
            newest
        );
        ClientPresetAction action = ClientInterfacePlan.Preset(
            status.MatchesPreset,
            status.HotSRunning,
            buildSealed
        );
        activity?.SetTag("client.matches_preset", status.MatchesPreset);
        activity?.SetTag("client.hots_running", status.HotSRunning);
        activity?.SetTag("client.interface_action", action.ToString());
        if (action == ClientPresetAction.Keep)
        {
            logger.LogInformation(
                "Heroes client already windowed 1080p with background audio and AhliObs."
            );
            return;
        }

        if (action == ClientPresetAction.LeaveRunning)
        {
            if (ClientInterfacePlan.CheckedBeforeLaunch(replayId, clientPreparedFor))
            {
                // The files were checked right before this client started. The running
                // client is left alone, and the next launch repairs them again (#206).
                logger.LogInformation(
                    "Replay {ReplayId} started after Variables.txt was checked. The running client is left alone ({Mismatches}).",
                    replayId,
                    string.Join("; ", status.Mismatches)
                );
                return;
            }

            logger.LogWarning(
                "Heroes client is not windowed 1080p with background audio and AhliObs ({Mismatches}). Quit the game and run `heroesreplay client configure`, then relaunch windowed.",
                string.Join("; ", status.Mismatches)
            );
            return;
        }

        ClientConfigureResult result = clientConfigurator.Configure();
        logger.LogInformation(
            "Applied windowed 1080p, background audio, and AhliObs to {Variables}. Interface copied: {Copied}. Installed build {Build}.",
            result.VariablesPath,
            result.InterfaceCopied,
            newest ?? "(unknown)"
        );
    }

    private void RememberInterfaceBuild()
    {
        string newest = ClientInterfacePlan.Newest(
            InstalledClientCatalog.FileVersions(settings.Location?.GameInstallDirectory)
        );
        ClientInterfaceSeal.Write(
            ClientInterfaceSeal.FilePath(settings.Location?.DataDirectory),
            newest
        );
    }
}
