using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Obs;
using HeroesReplay.Core.Shared;

using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameManager : IGameManager
    {
        private readonly Settings settings;
        private readonly ISessionCreator sessionCreater;
        private readonly IGameSession session;
        private readonly IHeroesProfileService heroesProfileService;
        private readonly IGameController gameController;
        private readonly ISessionHolder sessionHolder;
        private readonly IObsController obsController;
        private readonly ReplayDetailsWriter replayDetailsWriter;

        public GameManager(Settings settings, ISessionCreator sessionCreator, IGameSession session, IHeroesProfileService heroesProfileService, IGameController gameController, ISessionHolder sessionHolder, IObsController obsController, ReplayDetailsWriter replayDetailsWriter)
        {
            this.settings = settings;
            this.sessionCreater = sessionCreator;
            this.session = session;
            this.heroesProfileService = heroesProfileService;
            this.gameController = gameController;
            this.sessionHolder = sessionHolder;
            this.obsController = obsController;
            this.replayDetailsWriter = replayDetailsWriter;
        }

        public async Task SetSessionAsync(StormReplay stormReplay)
        {
            await sessionCreater.CreateAsync(stormReplay);

            if (settings.ReplayDetailsWriter.Enabled)
            {
                await replayDetailsWriter.WriteDetailsAsync(stormReplay);
            }

            if (settings.HeroesProfileApi.EnableMMR && sessionHolder.StormReplay.ReplayId.HasValue)
            {
                await obsController.UpdateMMRTierAsync(await heroesProfileService.GetMMRTier(sessionHolder.StormReplay));
            }

            await gameController.LaunchAsync(stormReplay);
        }

        public async Task SpectateSessionAsync()
        {
            if (settings.OBS.Enabled)
            {
                await obsController.SwapToGameSceneAsync();
                await session.SpectateAsync();
                gameController.KillGame();
                await replayDetailsWriter.ClearDetailsAsync();

                if (sessionHolder.StormReplay.ReplayId.HasValue)
                {
                    await obsController.CycleReportAsync(sessionHolder.StormReplay.ReplayId.Value);
                }

                await obsController.SwapToWaitingSceneAsync();
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