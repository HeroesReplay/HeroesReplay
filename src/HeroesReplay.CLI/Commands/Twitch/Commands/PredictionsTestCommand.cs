using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI;
using HeroesReplay.Core.Services.Twitch;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.Twitch.Commands;

public class PredictionsTestCommand : Command
{
    public PredictionsTestCommand()
        : base(
            "test",
            "Create a 30s Blue/Red prediction on the channel, then resolve or cancel it."
        )
    {
        Option<string> outcome = new("--outcome")
        {
            Description = "Blue, Red, or cancel (default cancel).",
            DefaultValueFactory = _ => "cancel",
        };
        Options.Add(outcome);
        SetAction(
            async (parseResult, cancellationToken) =>
            {
                await CommandAsync(parseResult.GetValue(outcome), cancellationToken);
            }
        );
    }

    private static async Task CommandAsync(string outcome, CancellationToken cancellationToken)
    {
        int? team = ParseOutcome(outcome);
        using ServiceProvider provider = new ServiceCollection()
            .AddCheckServices(cancellationToken)
            .AddSingleton<IMatchPredictionService, TwitchMatchPredictionService>()
            .BuildHeroesReplayProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        IMatchPredictionService predictions =
            provider.GetRequiredService<IMatchPredictionService>();
        await predictions.TestAsync(team, cancellationToken);
    }

    private static int? ParseOutcome(string outcome)
    {
        if (string.Equals(outcome, "blue", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(outcome, "red", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return null;
    }
}
