using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;

using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Twitch.Rewards;

using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Twitch.Commands
{
    public class SubmitCommand : Command
    {
        public SubmitCommand() : base("submit", $"submits the rewards from the config file and creates or updates the channel point rewards in the channel.")
        {
            Handler = CommandHandler.Create<CancellationToken>(CommandAsync);
        }

        protected async Task CommandAsync(CancellationToken cancellationToken)
        {
            using (ServiceProvider provider = new ServiceCollection().AddTwitchServices(cancellationToken).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }))
            {
                using (IServiceScope scope = provider.CreateScope())
                {
                    // Initialize data
                    var gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
                    await gameData.LoadDataAsync();

                    ITwitchRewardsManager rewardsManager = scope.ServiceProvider.GetRequiredService<ITwitchRewardsManager>();
                    await rewardsManager.CreateOrUpdateAsync();
                }
            }
        }
    }
}