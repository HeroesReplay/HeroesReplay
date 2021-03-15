using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Services;
using HeroesReplay.Service.Twitch.Core.Bot;

namespace HeroesReplay.Service.Twitch.Core
{
    public class TwitchService : BackgroundService
    {
        private readonly ITwitchBot twitchBot;

        public TwitchService(ITwitchBot twitchBot, CancellationTokenSource cts) : base(cts)
        {
            this.twitchBot = twitchBot;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var waiter = new ManualResetEventSlim())
            {
                await twitchBot.StartAsync();
                waiter.Wait(stoppingToken);
                await twitchBot.StopAsync();
            }
        }
    }
}
