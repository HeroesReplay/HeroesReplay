using Heroes.ReplayParser;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public class ReplayService
    {
        private readonly ILogger<ReplayService> logger;
        private readonly GameProvider provider;
        private readonly GameController controller;

        public ReplayService(ILogger<ReplayService> logger, GameProvider provider, GameController controller)
        {
            this.logger = logger;
            this.provider = provider;
            this.controller = controller;
        }

        public async Task RunAsync(CancellationToken token)
        {
            while(!token.IsCancellationRequested)
            {
                provider.LoadReplays();

                while (provider.Any())
                {
                    try 
                    {
                        (bool success, Game game) = await provider.TryLoadAsync();

                        if (success)
                        {
                            await controller.RunAsync(game, token);
                        }
                    }
                    catch (Exception)
                    {

                    }

                    logger.LogInformation("Replays in queue: " + provider.Count);
                }

                logger.LogInformation("Finished all replays in queue.");

                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
    }
}
