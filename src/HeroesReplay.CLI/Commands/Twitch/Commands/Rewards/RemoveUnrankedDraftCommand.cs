using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class RemoveUnrankedDraftCommand : Command
{
    public RemoveUnrankedDraftCommand()
        : base(
            "remove-unranked-draft",
            "Delete channel-point rewards titled Unranked Draft or (UD). Leaves Quick Match, Storm League, and ARAM rewards."
        )
    {
        SetAction(async (parseResult, cancellationToken) => await CommandAsync(cancellationToken));
    }

    private static async Task<int> CommandAsync(CancellationToken cancellationToken)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(cancellationToken)
            .BuildHeroesReplayProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using IServiceScope scope = provider.CreateScope();
        ITwitchRewardsManager rewardsManager =
            scope.ServiceProvider.GetRequiredService<ITwitchRewardsManager>();
        UnrankedDraftRewardRemoval removed = await rewardsManager.DeleteUnrankedDraftRewardsAsync();
        Console.WriteLine($"removed unranked draft rewards: {removed.Deleted.Count}");
        foreach (string title in removed.Deleted)
        {
            Console.WriteLine("  deleted " + title);
        }

        foreach (string title in removed.Failed)
        {
            Console.WriteLine("  failed " + title);
        }

        return removed.Failed.Count == 0 ? 0 : 1;
    }
}
