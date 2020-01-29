using System.Threading.Tasks;
using HeroesReplay.Replays;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Runner
{
    public class StormReplayConsumer
    {
        private readonly CancellationTokenProvider tokenProvider;
        private readonly ILogger<StormReplayConsumer> logger;
        private readonly IStormReplayProvider provider;
        private readonly StormReplayRunner runner;
        private readonly StormReplayDetailsWriter stormReplayDetailsWriter;

        public StormReplayConsumer(CancellationTokenProvider tokenProvider, ILogger<StormReplayConsumer> logger, IStormReplayProvider provider, StormReplayRunner runner, StormReplayDetailsWriter stormReplayDetailsWriter)
        {
            this.tokenProvider = tokenProvider;
            this.logger = logger;
            this.provider = provider;
            this.runner = runner;
            this.stormReplayDetailsWriter = stormReplayDetailsWriter;
        }

        public async Task ReplayAsync(bool launch)
        {
            while (!tokenProvider.Token.IsCancellationRequested)
            {
                StormReplay? stormReplay = await provider.TryLoadReplayAsync();

                if (stormReplay != null)
                {
                    logger.LogInformation("Loaded: " + stormReplay.Path);

                    await stormReplayDetailsWriter.WriteDetailsAsync(stormReplay);

                    await runner.ReplayAsync(stormReplay, launch);
                }
            }
        }
    }
}
