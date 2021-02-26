using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Stream;

namespace HeroesReplay.Core.Services.Observer
{
    public class GameManager : IGameManager
    {
        private readonly AppSettings settings;
        private readonly IReplayContextSetter contextSetter;
        private readonly ISpectator spectator;
        private readonly IGameController gameController;
        private readonly IObsController obsController;
        private readonly IReplayDetailsWriter replayDetailsWriter;

        public GameManager(AppSettings settings, IReplayContextSetter contextSetter, ISpectator spectator, IGameController gameController, IObsController obsController, IReplayDetailsWriter replayDetailsWriter)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.contextSetter = contextSetter ?? throw new ArgumentNullException(nameof(contextSetter));
            this.spectator = spectator ?? throw new ArgumentNullException(nameof(spectator));
            this.gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
            this.obsController = obsController ?? throw new ArgumentNullException(nameof(obsController));
            this.replayDetailsWriter = replayDetailsWriter ?? throw new ArgumentNullException(nameof(replayDetailsWriter));
        }

        public async Task LaunchAndSpectate(LoadedReplay loadedReplay)
        {
            contextSetter.SetContext(loadedReplay);

            await gameController.LaunchAsync();

            if (settings.OBS.Enabled)
            {
                await replayDetailsWriter.WriteFileForObs();
                obsController.SetRankImage();
                obsController.SwapToGameScene();
                await spectator.SpectateAsync();
                gameController.Kill();
                await replayDetailsWriter.ClearFileForObs();
                await obsController.CycleReportAsync();
                obsController.SwapToWaitingScene();
            }
            else
            {
                await spectator.SpectateAsync();
                gameController.Kill();
                await replayDetailsWriter.ClearFileForObs();
            }
        }
    }
}