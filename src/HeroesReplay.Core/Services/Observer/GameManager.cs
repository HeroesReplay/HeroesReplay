using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Client;
using HeroesReplay.Core.Services.Clips;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Media;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Retention;
using HeroesReplay.Core.Services.Status;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

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
    private readonly IYouTubeReplayLookup youTubeReplayLookup;
    private readonly RecordingClock recordingClock;
    private readonly ILogger<GameManager> logger;
    private readonly MediaPolicyAttemptLog mediaPolicy;
    private readonly IGameData gameData;

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
        IGameData gameData
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
    }

    public async Task<ReplaySessionKind> LaunchAndSpectate(
        LoadedReplay loadedReplay,
        Func<Task<LoadedReplay>> whileReporting
    )
    {
        MediaRetention.SweepAndLog(settings, logger);
        await MarkExistingYouTubeVideoAsync(loadedReplay).ConfigureAwait(false);
        MediaPolicySnapshot preLaunch = await mediaPolicy
            .RecordPreLaunchAsync(loadedReplay, settings.ReplayMedia, CancellationToken.None)
            .ConfigureAwait(false);
        ApplyPreLaunchPolicy(loadedReplay, preLaunch);
        await contextSetter.SetContextAsync(loadedReplay);
        bool obsSession = false;
        bool enteredMatch = false;
        statusStore.Patch(status =>
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
            status.GatesOpen = context.Current?.GatesOpen.ToString();
            status.CoreKilled = context.Current?.CoreKilled.ToString();
        });

        try
        {
            await WaitUntilDiskAllowsAsync().ConfigureAwait(false);
            using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.spectate");
            HeroesReplayTelemetry.TagReplay(
                activity,
                loadedReplay?.FileInfo?.FullName,
                loadedReplay?.Replay?.Map,
                loadedReplay?.ReplayId,
                loadedReplay?.Replay?.ReplayVersion
            );

            EnsureWindowedClient();
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
                RecordRedemption(loadedReplay, spectator.Outcome);
                return ReplaySession.Classify(spectator.Outcome);
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
                                logger
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
                    UnpublishRecording(loadedReplay, stopped, allowsMedia);
                }
            }

            if (enteredMatch)
            {
                await RecordPublicationAsync(loadedReplay, stopped).ConfigureAwait(false);
                ReplayShutdown.CaptureEndThenKill(gameController, logger);
            }
        }

        try
        {
            // A verified match or an award screen preloads the next replay and runs the report scenes.
            if (
                enteredMatch
                && obsSession
                && (
                    spectator.Outcome == MatchOutcome.VerifiedCompleted
                    || spectator.Outcome == MatchOutcome.AwardScreen
                )
            )
            {
                Task<LoadedReplay> nextLoad = InvokeNextLoad(whileReporting);
                using var cutReport = new CancellationTokenSource();
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

        RecordRedemption(loadedReplay, spectator.Outcome);
        return ReplaySession.Classify(spectator.Outcome);
    }

    public MatchOutcome LastOutcome => spectator.Outcome;

    public void ReleaseClientAfterDefer()
    {
        logger.LogWarning(
            "Closing Heroes so the next replay can start. Battle.net was not clicked."
        );
        gameController.Kill();
    }

    private async Task WaitUntilDiskAllowsAsync()
    {
        while (true)
        {
            DiskBacklogDecision decision = SpectateAdmission.Evaluate(MeasureDisk(), settings.Disk);
            if (SpectateAdmission.MayStart(decision))
            {
                if (decision.Pressure == DiskPressure.Warning)
                {
                    logger.LogWarning(
                        "Disk is in warning ({Reason}). Spectate continues.",
                        decision.Reason
                    );
                }

                return;
            }

            logger.LogWarning(
                "Disk {Reason}. New spectating waits. Recordings already on disk stay.",
                decision.Reason
            );
            await Task.Delay(TimeSpan.FromMinutes(1)).ConfigureAwait(false);
        }
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

    private void RecordRedemption(LoadedReplay loaded, MatchOutcome outcome)
    {
        Guid redemptionId = loaded?.RewardQueueItem?.Request?.RedemptionId ?? Guid.Empty;
        RedemptionEnd end = RedemptionDisposition.Decide(
            redemptionId != Guid.Empty,
            outcome == MatchOutcome.VerifiedCompleted
        );
        if (end == RedemptionEnd.None || settings.Location?.DataDirectory == null)
        {
            return;
        }

        string status = RewardRedemptionStatus.Decide(RewardRedemptionStatus.FromOutcome(outcome));
        string path = Path.Combine(settings.Location.DataDirectory, "redemption-dispositions.txt");
        RedemptionDispositionLog.Append(path, loaded.ReplayId, redemptionId, end);
        logger.LogInformation(
            "Redemption {RedemptionId} for replay {ReplayId} is {Status}. Twitch was not called.",
            redemptionId,
            loaded.ReplayId,
            status
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

        DateTimeOffset exitBy = DateTimeOffset.UtcNow.AddSeconds(20);
        while (gameController.IsGameRunning() && DateTimeOffset.UtcNow < exitBy)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
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
        await Task.Delay(ClientRelaunch.SettleAfterExit).ConfigureAwait(false);
        TimeSpan beforeNext = NextReplayHold.Duration(
            settings.OBS?.BeforeNextReplay ?? NextReplayHold.Default
        );
        await HoldBeforeNextLaunchAsync(report, beforeNext).ConfigureAwait(false);

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
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
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

            TimeSpan? matchClock = gameController.TryReadMatchClock();
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
                    "Next replay {ReplayId} is on the map loading screen or the on-screen timer. The report stops so OBS shows the game.",
                    next.ReplayId
                );
                cutReport.Cancel();
                return NextMatchLaunch.Presented;
            }

            if (NextReplayHold.StopWhenReportEnds(beforeNext) && report.IsCompleted)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }

        if (!gameController.IsGameRunning())
        {
            return NextMatchLaunch.NotStarted;
        }

        logger.LogWarning(
            "Next replay {ReplayId} is open, but the loading screen and the match clock were not seen. The report scene stays.",
            next.ReplayId
        );
        return NextMatchLaunch.ProcessOnly;
    }

    private async Task HoldBeforeNextLaunchAsync(Task report, TimeSpan hold)
    {
        if (hold <= TimeSpan.Zero)
        {
            return;
        }

        logger.LogInformation(
            "Waiting {Hold} before launching the next replay so prediction-report, match-report, and request-queue can finish.",
            hold
        );
        Task pause = Task.Delay(hold);
        Task finished = await Task.WhenAny(pause, report).ConfigureAwait(false);
        if (ReferenceEquals(finished, report))
        {
            await ObserveReportAsync(report).ConfigureAwait(false);
            ShowWaitingSceneBeforeNextLaunch();
            await pause.ConfigureAwait(false);
            return;
        }

        if (!report.IsCompleted)
        {
            logger.LogInformation(
                "The pause elapsed and a report scene is still on screen. The next replay waits until that cycle finishes."
            );
            await ObserveReportAsync(report).ConfigureAwait(false);
        }

        ShowWaitingSceneBeforeNextLaunch();
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

        loaded.PolicyAllowsRecording = snapshot?.Decision?.Record == true;
        loaded.PolicyAllowsPublication = false;
    }

    private async Task RecordPublicationAsync(
        LoadedReplay loadedReplay,
        ObsRecordingResult recording
    )
    {
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
        }
    }

    private static int PublishedThisDay(AppSettings settings)
    {
        PublicationLedger ledger = PublicationLedgerStore.Load(settings?.Location?.DataDirectory);
        if (ledger?.PublicAtUtc == null)
        {
            return 0;
        }

        return PublicationSchedule.PublishedIn(
            ledger.PublicAtUtc,
            DateTimeOffset.UtcNow,
            TimeSpan.FromHours(24)
        );
    }

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
                "OBS recording for replay {ReplayId} waits until the loading screen or the match clock is visible.",
                loadedReplay?.ReplayId
            );
            return;
        }

        ObsRecordingResult started = obsController.StartRecording();
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

    private void UnpublishRecording(
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
        bool removedFile = TryDelete(RecordingOwnership.FileToDiscard(stopped, allowsMedia));
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

    private void EnsureWindowedClient()
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
