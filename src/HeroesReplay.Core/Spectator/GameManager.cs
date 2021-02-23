using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Obs;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameManager : IGameManager
    {
        private readonly AppSettings settings;
        private readonly ISessionCreator sessionCreater;
        private readonly IGameSession session;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly IGameController gameController;
        private readonly ISessionHolder sessionHolder;
        private readonly IObsController obsController;
        private readonly IReplayDetailsWriter replayDetailsWriter;

        public GameManager(AppSettings settings, ISessionCreator sessionCreator, IGameSession session, IHeroesProfileService heroesProfileService, IGameController gameController, ISessionHolder sessionHolder, IObsController obsController, IReplayDetailsWriter replayDetailsWriter)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            sessionCreater = sessionCreator ?? throw new ArgumentNullException(nameof(sessionCreator));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.heroesProfileService = heroesProfileService ?? throw new ArgumentNullException(nameof(heroesProfileService));
            this.gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
            this.sessionHolder = sessionHolder ?? throw new ArgumentNullException(nameof(sessionHolder));
            this.obsController = obsController ?? throw new ArgumentNullException(nameof(obsController));
            this.replayDetailsWriter = replayDetailsWriter ?? throw new ArgumentNullException(nameof(replayDetailsWriter));
        }

        public async Task SpectateAsync(StormReplay stormReplay)
        {
            await SetSessionAsync(stormReplay);
            await SpectateSessionAsync();
        }

        private async Task SetSessionAsync(StormReplay stormReplay)
        {
            if (stormReplay == null)
                throw new ArgumentNullException(nameof(stormReplay));

            await sessionCreater.CreateAsync(stormReplay);
            await replayDetailsWriter.WriteObsDetails();

            if (settings.OBS.Enabled)
            {
                obsController.UpdateMMRTier();
            }

            await gameController.LaunchAsync(stormReplay);
        }

        private async Task SpectateSessionAsync()
        {
            if (settings.OBS.Enabled)
            {
                obsController.SwapToGameScene();
                await session.SpectateAsync();
                gameController.KillGame();
                await replayDetailsWriter.ClearDetailsAsync();
                await obsController.CycleReportAsync();
                obsController.SwapToWaitingScene();
            }
            else
            {
                await session.SpectateAsync();
                gameController.KillGame();
                await replayDetailsWriter.ClearDetailsAsync();
            }
        }
    }
}