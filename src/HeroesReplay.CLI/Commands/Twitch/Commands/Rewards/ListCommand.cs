using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class ListCommand : Command
{
    public ListCommand()
        : base("list", "List custom channel-point rewards on the Twitch channel.")
    {
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                await CommandAsync(cancellationToken);
            }
        );
    }

    private static async Task CommandAsync(CancellationToken cancellationToken)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(cancellationToken)
            .BuildHeroesReplayProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using IServiceScope scope = provider.CreateScope();
        IGameData gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
        await gameData.LoadDataAsync();
        ITwitchRewardsManager rewardsManager =
            scope.ServiceProvider.GetRequiredService<ITwitchRewardsManager>();
        var titles = await rewardsManager.ListRemoteTitlesAsync();
        Console.WriteLine($"channel rewards: {titles.Count}");
        foreach (string title in titles)
        {
            Console.WriteLine("  " + title);
        }
    }
}
