using System.Threading.Tasks;
using HeroesReplay.Replays;
using HeroesReplay.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Runner
{
    public class StormReplayConsumer
    {
        private readonly ILogger<StormReplayConsumer> logger;
        private readonly IStormReplayProvider provider;
        private readonly StormReplayRunner runner;

        public StormReplayConsumer(ILogger<StormReplayConsumer> logger, IStormReplayProvider provider, StormReplayRunner runner)
        {
            this.logger = logger;
            this.provider = provider;
            this.runner = runner;
        }

        public async Task ReplayAsync(bool launch)
        {
            StormReplay? stormReplay;

            do
            {
                stormReplay = await provider.TryLoadReplayAsync();

                if (stormReplay != null)
                {
                    logger.LogInformation("Loaded: " + stormReplay.Path);

                    await runner.ReplayAsync(stormReplay, launch);
                }
            }
            while (stormReplay != null);
        }
    }
}
