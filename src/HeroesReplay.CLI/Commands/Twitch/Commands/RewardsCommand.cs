using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Twitch;
using HeroesReplay.Core.Services.Twitch.Rewards;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Twitch.Commands
{
    public class RewardsCommand : Command
    {
        public RewardsCommand() : base("rewards", $"Creates or updates the custom rewards for the channel")
        {
            AddCommand(new GenerateCommand());
            AddCommand(new SubmitCommand());
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddTwitchServices(cancellationToken).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
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