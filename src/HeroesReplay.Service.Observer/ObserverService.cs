using System;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services;
using HeroesReplay.Core.Services.Observer;
using HeroesReplay.Core.Services.Providers;

using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core
{
    public class ObserverService : BackgroundService
    {
        private readonly ILogger<ObserverService> logger;
        private readonly IGameManager gameManager;
        private readonly IReplayProvider replayProvider;

        public ObserverService(ILogger<ObserverService> logger, IGameManager gameManager, IReplayProvider replayProvider, CancellationTokenSource cts) : base(cts)
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
                    await Task.Delay(TimeSpan.FromMinutes(1.5), stoppingToken);
                }
            }
        }
    }
}