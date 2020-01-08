using Heroes.ReplayParser;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public class ReplayService
    {
        private readonly GameProvider provider;
        private readonly GameController controller;

        public ReplayService(GameProvider provider, GameController controller)
        {
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
                }

                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
        }
    }
}
