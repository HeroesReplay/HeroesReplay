using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Picker;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Runner
{
    public sealed class ReplayProcessor
    {
        private readonly CancellationTokenProvider tokenProvider;
        private readonly ILogger<ReplayConsumer> logger;
        private readonly IReplayProvider provider;
        private readonly IReplaySaver saver;
        private readonly ReplayPicker picker;

        public ReplayProcessor(ILogger<ReplayConsumer> logger, IReplayProvider provider, IReplaySaver saver, ReplayPicker picker, CancellationTokenProvider tokenProvider)
        {
            this.logger = logger;
            this.provider = provider;
            this.saver = saver;
            this.picker = picker;
            this.tokenProvider = tokenProvider;
        }

        public async Task RunAsync()
        {
            try
            {
                while (!tokenProvider.Token.IsCancellationRequested)
                {
                    StormReplay? stormReplay = await provider.TryLoadReplayAsync();

                    if (stormReplay != null)
                    {
                        logger.LogInformation("Loaded: " + stormReplay.Path);

                        if (picker.IsInteresting(stormReplay))
                        {
                            await saver.SaveReplayAsync(stormReplay).ConfigureAwait(false);
                        }
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
