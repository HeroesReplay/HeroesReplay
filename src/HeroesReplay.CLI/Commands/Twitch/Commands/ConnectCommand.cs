using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Providers;
using HeroesReplay.Core.Services.Twitch;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Twitch.Commands
{
    public class ConnectCommand : Command
    {
        public ConnectCommand() : base("connect", $"Connect to the twitch channel for HeroesReplay.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddSpectateServices(cancellationToken, typeof(HeroesProfileProvider)).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    using (var waiter = new ManualResetEventSlim())
                    {
                        var gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
                        await gameData.LoadDataAsync();

                        ITwitchBot twitchBot = scope.ServiceProvider.GetRequiredService<ITwitchBot>();
                        await twitchBot.StartAsync();
                        waiter.Wait(cancellationToken);
                        await twitchBot.StopAsync();
                    }
                }
            }
        }
    }
}