using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.Obs;
using HeroesReplay.Core.Shared;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameManager : IGameManager
    {
        private readonly ISessionCreator sessionCreater;
        private readonly IGameSession session;
        private readonly IGameController gameController;
        private readonly ISessionHolder sessionHolder;
        private readonly IObsController obsController;
        private readonly ReplayFileWriter replayFileWriter;

        public GameManager(ISessionCreator sessionCreator, IGameSession session, IGameController gameController, ISessionHolder sessionHolder, IObsController obsController, ReplayFileWriter replayFileWriter)
        {
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
            await gameController.LaunchAsync(stormReplay);
            await replayFileWriter.WriteDetailsAsync(stormReplay);
        }

        public async Task SpectateSessionAsync()
        {
            await session.SpectateAsync();
            
            gameController.KillGame();

            if (sessionHolder.StormReplay.ReplayId.HasValue)
            {
                await obsController.CycleScenesAsync(sessionHolder.StormReplay.ReplayId.Value);
            }

            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
}