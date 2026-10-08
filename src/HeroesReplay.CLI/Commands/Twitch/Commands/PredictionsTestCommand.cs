using System;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.Twitch.Predictions;
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
        outcome.Validators.Add(result =>
        {
            string value = result.GetValueOrDefault<string>();
            if (!TryParseOutcome(value, out _))
            {
                result.AddError($"--outcome must be Blue, Red, or cancel. '{value}' is not one.");
            }
        });
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
        TryParseOutcome(outcome, out int? team);
        using ServiceProvider provider = AddServices(new ServiceCollection(), cancellationToken)
            .BuildHeroesReplayProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        IMatchPredictionService predictions =
            provider.GetRequiredService<IMatchPredictionService>();
        await predictions.TestAsync(team, cancellationToken);
    }

    /// <summary>
    /// The services this command resolves: the check services and the prediction service,
    /// registered as <c>twitch connect</c> registers it. No chat client is registered, so the
    /// verdict goes to the report page only.
    /// </summary>
    public static IServiceCollection AddServices(
        IServiceCollection services,
        CancellationToken cancellationToken
    ) => services.AddCheckServices(cancellationToken).AddMatchPredictionServices();

    /// <summary>Blue is team 0, Red is team 1, and cancel is no team.</summary>
    public static bool TryParseOutcome(string outcome, out int? team)
    {
        team = null;
        if (string.Equals(outcome, "blue", StringComparison.OrdinalIgnoreCase))
        {
            team = 0;
            return true;
        }

        if (string.Equals(outcome, "red", StringComparison.OrdinalIgnoreCase))
        {
            team = 1;
            return true;
        }

        return string.Equals(outcome, "cancel", StringComparison.OrdinalIgnoreCase);
    }
}
