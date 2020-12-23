using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Shared;

using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameManager : IGameManager
    {
        private readonly ISessionCreator sessionCreater;
        private readonly IGameSession session;
        private readonly IGameController gameController;
        private readonly ReplayFileWriter replayFileWriter;

        public GameManager(ISessionCreator sessionCreator, IGameSession session, IGameController controller, ReplayFileWriter replayFileWriter)
        {
            this.sessionCreater = sessionCreator;
            this.session = session;
            this.gameController = controller;
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
        }
    }
}