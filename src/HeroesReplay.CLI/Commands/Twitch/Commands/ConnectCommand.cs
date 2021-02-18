using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core;
using HeroesReplay.Core.Providers;
using HeroesReplay.Core.Twitch;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class ConnectCommand : Command
    {
        public ConnectCommand() : base("connect", $"Connect to the twitch channel for Heroes Replay.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (IServiceScope scope = new ServiceCollection().AddTwitchServices(cancellationToken).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }).CreateScope())
            {
                using (var waiter = new ManualResetEventSlim())
                {
                    var twitchService = scope.ServiceProvider.GetRequiredService<HeroesReplayTwitchService>();
                    twitchService.Initialize();

                    waiter.Wait(cancellationToken);
                }
            }
        }
    }
}