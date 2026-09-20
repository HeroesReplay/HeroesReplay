using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using Microsoft.Extensions.DependencyInjection;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class TestCommand : Command
{
    public TestCommand()
        : base(
            "test",
            "Run the local reward handler as if a viewer redeemed a channel-point reward."
        )
    {
        Option<string> title = new("--title")
        {
            Description = "Reward title, e.g. Random (SL).",
            DefaultValueFactory = _ => "Random (SL)",
        };
        Options.Add(title);
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                await CommandAsync(parseResult.GetValue(title), cancellationToken);
            }
        );
    }

    private static async Task CommandAsync(string title, CancellationToken cancellationToken)
    {
        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(cancellationToken)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        using IServiceScope scope = provider.CreateScope();
        IGameData gameData = scope.ServiceProvider.GetRequiredService<IGameData>();
        await gameData.LoadDataAsync();
        IOnRewardHandler handler = scope.ServiceProvider.GetRequiredService<IOnRewardHandler>();
        handler.Handle(
            new OnRewardRedeemedArgs
            {
                RewardTitle = title,
                DisplayName = "test",
                Login = "test",
                Message = string.Empty,
                RedemptionId = Guid.NewGuid(),
                ChannelId = "487238352",
            }
        );
        await Task.Delay(2000, cancellationToken);
        Console.WriteLine("dispatched reward '" + title + "'");
    }
}
