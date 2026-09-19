using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HeroesReplay.Core;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Client;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Status;
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
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task LaunchAndSpectate(LoadedReplay loadedReplay)
    {
        await contextSetter.SetContextAsync(loadedReplay);
        bool obsSession = false;
        statusStore.Patch(status =>
        {
            status.SpectatorRunning = true;
            status.Phase = "Loading";
            status.Map = loadedReplay?.Replay?.Map;
            status.ReplayPath = loadedReplay?.FileInfo?.FullName;
            status.ReplayVersion = loadedReplay?.Replay?.ReplayVersion;
            status.ReplayId = loadedReplay?.ReplayId;
            status.GatesOpen = context.Current?.GatesOpen.ToString();
            status.CoreKilled = context.Current?.CoreKilled.ToString();
        });

        try
        {
            using Activity activity = HeroesReplayTelemetry.ActivitySource.StartActivity(
                "heroesreplay.spectate"
            );
            activity?.SetTag("replay.path", loadedReplay?.FileInfo?.FullName);
            activity?.SetTag("replay.map", loadedReplay?.Replay?.Map);
            activity?.SetTag("replay.version", loadedReplay?.Replay?.ReplayVersion);
            activity?.SetTag("replay.id", loadedReplay?.ReplayId);

            EnsureWindowedClient();
            await gameController.LaunchAsync();

            if (settings.OBS.Enabled)
            {
                obsController.BeginSession();
                obsSession = true;
                statusStore.Patch(status => status.ObsSession = true);
                obsController.ConfigureFromContext();
                obsController.SwapToGameScene();
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
            }

            gameController.Kill();
        }

        try
        {
            if (obsSession)
            {
                await obsController.CycleReportAsync();
                obsController.SwapToWaitingScene();
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

    private void EnsureWindowedClient()
    {
        ClientStatusResult status = clientConfigurator.GetStatus();
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
