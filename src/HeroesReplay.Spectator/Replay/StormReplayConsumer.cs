using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Spectator
{
    public class StormReplayConsumer
    {
        private readonly ILogger<StormReplayConsumer> logger;
        private readonly StormReplayProvider provider;
        private readonly StormReplayRunner runner;
        private readonly CancellationToken token;

        public StormReplayConsumer(CancellationTokenProvider tokenProvider, ILogger<StormReplayConsumer> logger, StormReplayProvider provider, StormReplayRunner runner)
        {
            this.logger = logger;
            this.provider = provider;
            this.runner = runner;
            this.token = tokenProvider.Token;
        }

        public async Task ReplayAsync(string path, bool launch)
        {
            if (File.Exists(path))
            {
                StormReplay? stormReplay = await provider.TryLoadReplayAsync(path);

                if (stormReplay != null)
                {
                    await runner.ReplayAsync(stormReplay, launch);
                }
            }
            else if (Directory.Exists(path))
            {
                await QueueReplaysAndRunAsync(path, launch);
            }
        }

        private async Task QueueReplaysAndRunAsync(string path, bool launch)
        {
            while (!token.IsCancellationRequested)
            {
                provider.LoadReplays(path);

                try
                {
                    while (provider.Count > 0)
                    {
                        StormReplay? stormReplay = await provider.TryLoadReplayAsync();

                        if (stormReplay != null)
                        {
                            logger.LogInformation("Starting replay for: " + stormReplay.FilePath);

                            await runner.ReplayAsync(stormReplay, launch);
                        }
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error in loading replay");
                }

                logger.LogInformation("Replays in queue: " + provider.Count);
            }

            logger.LogInformation("Finished all replays in queue.");

            await Task.Delay(TimeSpan.FromSeconds(5), token);
        }
    }
}
