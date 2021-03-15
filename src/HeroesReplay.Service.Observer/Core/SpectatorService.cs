using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Service.Spectator.Core.Observer;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Service.Spectator.Core
{
    public class SpectatorService : BackgroundService
    {
        private readonly ILogger<SpectatorService> logger;
        private readonly IGameManager gameManager;
        private readonly IReplayProvider replayProvider;

        public SpectatorService(
            ILogger<SpectatorService> logger, 
            IGameManager gameManager, 
            IReplayProvider replayProvider, 
            CancellationTokenSource cts) : base(cts)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            this.replayProvider = replayProvider ?? throw new ArgumentNullException(nameof(replayProvider));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                LoadedReplay loadedReplay = await replayProvider.TryLoadNextReplayAsync();

                if (loadedReplay != null)
                {
                    await gameManager.LaunchAndSpectate(loadedReplay);
                }
            }
        }
    }
}