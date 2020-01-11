using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay
{
    public class TwitchService
    {
        private readonly GameRunner controller;

        public TwitchService(GameRunner controller)
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
