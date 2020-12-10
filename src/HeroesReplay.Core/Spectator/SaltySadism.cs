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
        private readonly IReplayProvider replayProvider;
        private readonly HeroesProfileMatchDetailsWriter matchDetailsWriter;
        private readonly CancellationTokenProvider tokenProvider;

        public SaltySadism(ILogger<SaltySadism> logger, IGameManager gameManager, IReplayProvider replayProvider, HeroesProfileMatchDetailsWriter matchDetailsWriter, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.gameManager = gameManager;
            this.replayProvider = replayProvider;
            this.matchDetailsWriter = matchDetailsWriter;
            this.tokenProvider = tokenProvider;
        }

        public async Task RunAsync()
        {
            try
            {
                while (!tokenProvider.Token.IsCancellationRequested)
                {
                    StormReplay? stormReplay = await replayProvider.TryLoadReplayAsync();

                    if (stormReplay != null)
                    {
                        await gameManager.SetSessionAsync(stormReplay)
                                         .ContinueWith(result => matchDetailsWriter.WriteDetailsAsync(stormReplay)).Unwrap()
                                         .ContinueWith(result => gameManager.SpectateSessionAsync()).Unwrap();
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "There was an error in the replay consumer.");
            }
            finally
            {

            }
        }
    }
}