using HeroesReplay.Core.Models;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public class SaltySadism
    {
        private readonly ILogger<SaltySadism> logger;
        private readonly IGameManager gameManager;
        private readonly IGameData gameData;
        private readonly IReplayProvider replayProvider;
        private readonly CancellationTokenProvider tokenProvider;

        public SaltySadism(ILogger<SaltySadism> logger, IGameManager gameManager, IGameData gameData, IReplayProvider replayProvider, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.gameManager = gameManager;
            this.gameData = gameData;
            this.replayProvider = replayProvider;
            this.tokenProvider = tokenProvider;
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