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

                if (provider.Any())
                {
                    try 
                    {
                        if (provider.TryDequeue(out Game game))
                        {
                            await controller.RunAsync(game, token);

                            provider.MoveToFinished(game);
                        }
                        else
                        {
                            continue;
                        }
                    }
                    catch(Exception)
                    {

                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10));
            }
        }
    }

    public class TwitchService
    {
        private readonly GameController controller;

        public TwitchService(GameController controller)
        {
            this.controller = controller;
        }

        public async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {                
                // controller.SendTogglePause();

                // controller.SendToggleChat();

                // controller.SendToggleTime();

                // controller.SendToggleBottomConsole();

                await Task.Delay(5000);
            }
        }
    }
}
