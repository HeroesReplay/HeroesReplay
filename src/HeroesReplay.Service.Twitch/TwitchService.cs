
using HeroesReplay.Core.Services;
using HeroesReplay.Core.Services.Twitch;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Service.Twitch
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
