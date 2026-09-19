using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;

namespace HeroesReplay.Core.Services.Observer;

public class GameManager : IGameManager
{
    private readonly AppSettings settings;
    private readonly IReplayContextSetter contextSetter;
    private readonly ISpectator spectator;
    private readonly IGameController gameController;
    private readonly IObsController obsController;

    public GameManager(
        AppSettings settings,
        IReplayContextSetter contextSetter,
        ISpectator spectator,
        IGameController gameController,
        IObsController obsController
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
    }

    public async Task LaunchAndSpectate(LoadedReplay loadedReplay)
    {
        await contextSetter.SetContextAsync(loadedReplay);
        bool obsSession = false;

        try
        {
            await gameController.LaunchAsync();

            if (settings.OBS.Enabled)
            {
                obsController.BeginSession();
                obsSession = true;
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
