using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Runner
{
    public class ReplayConsumer
    {
        private readonly CancellationTokenProvider tokenProvider;
        private readonly ILogger<ReplayConsumer> logger;
        private readonly IReplayProvider provider;
        private readonly ReplayRunner runner;
        private readonly ReplayDetailsWriter writer;

        public ReplayConsumer(
            ILogger<ReplayConsumer> logger,
            CancellationTokenProvider tokenProvider,
            IReplayProvider provider,
            ReplayRunner runner,
            ReplayDetailsWriter writer)
        {
            this.tokenProvider = tokenProvider;
            this.logger = logger;
            this.provider = provider;
            this.runner = runner;
            this.writer = writer;
        }

        public async Task RunAsync(bool launch)
        {
            try
            {
                while (!tokenProvider.Token.IsCancellationRequested)
                {
                    StormReplay? stormReplay = await provider.TryLoadReplayAsync();

                    if (stormReplay != null)
                    {
                        logger.LogInformation("Loaded: " + stormReplay.Path);

                        await writer.WriteDetailsAsync(stormReplay);

                        await runner.ReplayAsync(stormReplay, launch);
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
