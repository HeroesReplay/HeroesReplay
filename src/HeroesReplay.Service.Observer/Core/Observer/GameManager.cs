using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Service.Spectator.Core.Context;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Service.Spectator.Core.Observer
{
    public class GameManager : IGameManager
    {
        private readonly IOptions<AppSettings> settings;
        private readonly ISpectator spectator;
        private readonly IGameController gameController;
        private readonly IContextManager contextManager;

        public GameManager(IOptions<AppSettings> settings, IContextManager contextManager, ISpectator spectator, IGameController gameController)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.contextManager = contextManager ?? throw new ArgumentNullException(nameof(contextManager));
            this.spectator = spectator ?? throw new ArgumentNullException(nameof(spectator));
            this.gameController = gameController ?? throw new ArgumentNullException(nameof(gameController));
        }

        public async Task LaunchAndSpectate(LoadedReplay loadedReplay)
        {
            await contextManager.SetContextAsync(loadedReplay);
            await gameController.LaunchAsync();

            await spectator.SpectateAsync();
            gameController.Kill();
        }
    }
}