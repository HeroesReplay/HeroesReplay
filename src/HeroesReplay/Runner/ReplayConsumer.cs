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
        private readonly CancellationToken token;

        public ReplayConsumer(ILogger<ReplayConsumer> logger, StormReplayProvider stormReplayProvider, GameRunner gameRunner, CancellationTokenSource source)
        {
            this.logger = logger;
            this.stormReplayProvider = stormReplayProvider;
            this.gameRunner = gameRunner;
            this.token = source.Token;
        }

        public async Task RunAsync(string? replayFile = null, bool launchGame = true)
        {
            if (replayFile != null)
            {
                StormReplay? stormReplay = await stormReplayProvider.TryLoadReplayAsync(replayFile);

                if (stormReplay != null)
                {
                    await gameRunner.RunAsync(stormReplay, launchGame);
                }
            }
            else
            {
                await QueueReplaysAndRunAsync();
            }
        }

        private async Task QueueReplaysAndRunAsync()
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

                            await gameRunner.RunAsync(stormReplay, launchGame: true);
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
