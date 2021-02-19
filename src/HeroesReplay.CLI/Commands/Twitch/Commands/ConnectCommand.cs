using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Services.Twitch;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands
{
    public class ConnectCommand : Command
    {
        public ConnectCommand() : base("connect", $"Connect to the twitch channel for HeroesReplay.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddTwitchServices(cancellationToken).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    using (var waiter = new ManualResetEventSlim())
                    {
                        ITwitchBot twitchBot = scope.ServiceProvider.GetRequiredService<ITwitchBot>();
                        await twitchBot.ConnectAsync();
                        waiter.Wait(cancellationToken);
                    }
                }
            }
        }
    }
}