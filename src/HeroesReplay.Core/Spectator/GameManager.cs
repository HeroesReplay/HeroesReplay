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
        private readonly IGameController gameController;
        private readonly ISessionHolder sessionHolder;
        private readonly IObsController obsController;
        private readonly ReplayFileWriter replayFileWriter;

        public GameManager(Settings settings, ISessionCreator sessionCreator, IGameSession session, IGameController gameController, ISessionHolder sessionHolder, IObsController obsController, ReplayFileWriter replayFileWriter)
        {
            this.settings = settings;
            this.sessionCreater = sessionCreator;
            this.session = session;
            this.gameController = gameController;
            this.sessionHolder = sessionHolder;
            this.obsController = obsController;
            this.replayFileWriter = replayFileWriter;
        }

        public async Task SetSessionAsync(StormReplay stormReplay)
        {
            await sessionCreater.CreateAsync(stormReplay);
            await replayFileWriter.WriteDetailsAsync(stormReplay);
            await gameController.LaunchAsync(stormReplay);
        }

        public async Task SpectateSessionAsync()
        {
            if (settings.OBS.Enabled)
            {
                await obsController.SwapToGameSceneAsync();
                await session.SpectateAsync();
                gameController.KillGame();
                await replayFileWriter.ClearDetailsAsync();

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
                await replayFileWriter.ClearDetailsAsync();                
            }

            await Task.Delay(settings.Spectate.WaitingTime);
        }
    }
}