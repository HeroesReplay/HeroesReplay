using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay
{
    public class ReplayConsumer
    {
        private readonly ILogger<ReplayConsumer> logger;
        private readonly StormReplayProvider stormReplayProvider;
        private readonly GameRunner gameRunner;

        public ReplayConsumer(ILogger<ReplayConsumer> logger, StormReplayProvider stormReplayProvider, GameRunner gameRunner)
        {
            this.logger = logger;
            this.stormReplayProvider = stormReplayProvider;
            this.gameRunner = gameRunner;
        }

        public async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                stormReplayProvider.LoadReplays();

                try
                {
                    while (stormReplayProvider.Count > 0)
                    {
                        StormReplay? stormReplay = await stormReplayProvider.TryLoadReplayAsync();

                        if (stormReplay != null)
                        {
                            logger.LogInformation("Starting replay for: " + stormReplay.FilePath);

                            await gameRunner.RunAsync(stormReplay, token);
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error in loading replay");
                }

                logger.LogInformation("Replays in queue: " + stormReplayProvider.Count);
            }

            logger.LogInformation("Finished all replays in queue.");

            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
    }
}
