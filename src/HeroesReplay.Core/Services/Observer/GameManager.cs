using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Client;
using HeroesReplay.Core.Services.Clips;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Retention;
using HeroesReplay.Core.Services.Status;
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
        ILogger<GameManager> logger
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
    }

    public async Task LaunchAndSpectate(
        LoadedReplay loadedReplay,
        Func<Task<LoadedReplay>> whileReporting
    )
    {
        MediaRetention.SweepAndLog(settings, logger);
        await MarkExistingYouTubeVideoAsync(loadedReplay).ConfigureAwait(false);
        await contextSetter.SetContextAsync(loadedReplay);
        bool obsSession = false;
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
            status.SuppressPredictions = ReplayRequestKind.ViewerEnteredReplayId(loadedReplay);
            status.GatesOpen = context.Current?.GatesOpen.ToString();
            status.CoreKilled = context.Current?.CoreKilled.ToString();
        });

        try
        {
            using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.spectate");
            HeroesReplayTelemetry.TagReplay(
                activity,
                loadedReplay?.FileInfo?.FullName,
                loadedReplay?.Replay?.Map,
                loadedReplay?.ReplayId,
                loadedReplay?.Replay?.ReplayVersion
            );

            EnsureWindowedClient();
            await gameController.LaunchAsync();

            if (settings.OBS.Enabled)
            {
                obsController.BeginSession();
                obsSession = true;
                statusStore.Patch(status => status.ObsSession = true);
                obsController.ConfigureFromContext();
                if (SessionMedia.ShouldRecord(settings.OBS, loadedReplay))
                {
                    recordingClock.Start();
                }

                obsController.StartRecording();
            }

            await spectator.SpectateAsync();
        }
        finally
        {
            if (obsSession)
            {
                try
                {
                    obsController.StopRecording();
                }
                catch { }

                try
                {
                    await MatchClipExporter
                        .ExportAsync(
                            context.Current?.LoadedReplay?.Replay,
                            context.Current?.LoadedReplay?.ReplayId,
                            context.Current?.Directory?.FullName,
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

            ReplayShutdown.CaptureEndThenKill(gameController, logger);
        }

        try
        {
            if (obsSession)
            {
                NextGameSignal nextGame = new();
                Task<LoadedReplay> nextLoad = InvokeNextLoad(whileReporting);
                Task report = obsController.CycleReportAsync(nextGame);
                Task launch = LaunchNextDuringReportAsync(nextLoad, nextGame);
                await Task.WhenAll(report, launch).ConfigureAwait(false);
                if (!nextGame.IsSignaled)
                {
                    obsController.SwapToWaitingScene();
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

    private async Task LaunchNextDuringReportAsync(
        Task<LoadedReplay> nextLoad,
        NextGameSignal signal
    )
    {
        if (settings.Capture?.Method == CaptureMethod.None)
        {
            return;
        }

        LoadedReplay next;
        try
        {
            next = await nextLoad.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load the next replay during the report.");
            return;
        }

        if (next?.FileInfo == null || !next.FileInfo.Exists)
        {
            return;
        }

        DateTimeOffset exitBy = DateTimeOffset.UtcNow.AddSeconds(20);
        while (gameController.IsGameRunning() && DateTimeOffset.UtcNow < exitBy)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        }

        if (gameController.IsGameRunning())
        {
            logger.LogWarning("The previous game is still running. The next replay stays queued.");
            return;
        }

        try
        {
            Process.Start(
                new ProcessStartInfo { FileName = next.FileInfo.FullName, UseShellExecute = true }
            );
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not start the next replay during the report.");
            return;
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
            return;
        }

        signal.Signal();
        try
        {
            obsController.SwapToGameScene();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not switch OBS to the game scene.");
        }

        logger.LogInformation(
            "Next replay {ReplayId} is open. OBS is on the game scene.",
            next.ReplayId
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

    private void EnsureWindowedClient()
    {
        using Activity activity = HeroesReplayTelemetry.StartSpan("heroesreplay.client.configure");
        ClientStatusResult status = clientConfigurator.GetStatus();
        activity?.SetTag("client.matches_preset", status.MatchesPreset);
        activity?.SetTag("client.hots_running", status.HotSRunning);
        if (status.MatchesPreset)
        {
            logger.LogInformation("Heroes client already windowed 1080p with AhliObs.");
            return;
        }

        if (status.HotSRunning)
        {
            logger.LogWarning(
                "Heroes client is not windowed 1080p / AhliObs ({Mismatches}). Quit the game and run `heroesreplay client configure`, then relaunch windowed.",
                string.Join("; ", status.Mismatches)
            );
            return;
        }

        ClientConfigureResult result = clientConfigurator.Configure();
        logger.LogInformation(
            "Applied windowed 1080p + AhliObs to {Variables}. Interface copied: {Copied}.",
            result.VariablesPath,
            result.InterfaceCopied
        );
    }
}
