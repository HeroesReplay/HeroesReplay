using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class GameManager : IGameManager
    {
        private readonly ISessionCreator setter;
        private readonly IGameSession session;
        private readonly IGameController controller;

        public GameManager(ISessionCreator setter, IGameSession session, IGameController controller)
        {
            this.setter = setter;
            this.session = session;
            this.controller = controller;
        }

        public async Task SetSessionAsync(StormReplay stormReplay)
        {
            await setter.SetSessionAsync(stormReplay);
            await controller.LaunchAsync(stormReplay);
        }

        public async Task SpectateSessionAsync()
        {
            await session.SpectateAsync();
            controller.KillGame();
        }
    }
}