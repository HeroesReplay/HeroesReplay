using HeroesReplay.Core.Models;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class SpectateEngine : ISpectateEngine
    {
        private readonly ILogger<SpectateEngine> logger;
        private readonly IGameManager gameManager;
        private readonly IGameData gameData;
        private readonly IReplayProvider replayProvider;
        private readonly CancellationTokenProvider tokenProvider;

        public SpectateEngine(ILogger<SpectateEngine> logger, IGameManager gameManager, IGameData gameData, IReplayProvider replayProvider, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.gameManager = gameManager ?? throw new ArgumentNullException(nameof(gameManager));
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
            this.replayProvider = replayProvider ?? throw new ArgumentNullException(nameof(replayProvider));
            this.tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        }

        public async Task RunAsync()
        {
            try
            {
                await gameData.LoadDataAsync().ConfigureAwait(false);

                while (!tokenProvider.Token.IsCancellationRequested)
                {
                    StormReplay stormReplay = await replayProvider.TryLoadReplayAsync().ConfigureAwait(false);

                    if (stormReplay != null)
                    {
                        await gameManager.SetSessionAsync(stormReplay).ConfigureAwait(false);
                        await gameManager.SpectateSessionAsync().ConfigureAwait(false);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "There was an error in the replay consumer.");
            }
        }
    }
}