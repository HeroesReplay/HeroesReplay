using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Runner;

namespace HeroesReplay.Twitch
{
    public class TwitchService
    {
        private readonly StormReplayRunner controller;

        public TwitchService(StormReplayRunner controller)
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
