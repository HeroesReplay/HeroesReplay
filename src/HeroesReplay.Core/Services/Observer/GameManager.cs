using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using HeroesReplay.Core.Services.Status;

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

    public GameManager(
        AppSettings settings,
        IReplayContextSetter contextSetter,
        ISpectator spectator,
        IGameController gameController,
        IObsController obsController,
        IReplayContext context,
        SpectatorStatusStore statusStore
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
}
