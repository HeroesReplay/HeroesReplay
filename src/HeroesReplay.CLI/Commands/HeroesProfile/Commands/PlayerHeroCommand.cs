using System;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.CLI.OpenTelemetry;
using HeroesReplay.Core.HeroesProfile;
using Microsoft.Extensions.DependencyInjection;

namespace HeroesReplay.CLI.Commands.HeroesProfile.Commands;

/// <summary>
/// One player's Storm League record on one hero (#433), the lookup behind focus-hero picks:
/// Heroes Profile <c>players/heroes</c>, through the same cache, budget and pacing the download
/// role uses.
/// </summary>
public class PlayerHeroCommand : Command
{
    public PlayerHeroCommand()
        : base(
            "player-hero",
            "Look up one player's Storm League record on one hero (wins, games, win rate, KDA, hero MMR and tier) from Heroes Profile players/heroes, the lookup behind focus-hero picks. Uses and fills the cache under Data\\HeroesProfile\\players and counts against HeroesProfileApi:PlayerStats:WeeklyCallBudget. A private profile, a missing player and an unknown hero are answers too."
        )
    {
        var battleTagOption = new Option<string>("--battletag")
        {
            Description = "Name#1234.",
            Required = true,
        };
        var regionOption = new Option<string>("--region")
        {
            Description = "NA, EU, KR or CN (or 1, 2, 3 or 5).",
            Required = true,
        };
        var heroOption = new Option<string>("--hero")
        {
            Description = "The hero's English name, as Heroes Profile writes it (Kael'thas).",
            Required = true,
        };
        var attributeOption = new Option<string>("--attribute")
        {
            Description = "The hero's attribute id (Kael), to pick its row; default the name.",
        };
        var freshOption = new Option<bool>("--fresh")
        {
            Description = "Forget what the cache holds about this player first.",
        };
        Options.Add(battleTagOption);
        Options.Add(regionOption);
        Options.Add(heroOption);
        Options.Add(attributeOption);
        Options.Add(freshOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(battleTagOption),
                    parseResult.GetValue(regionOption),
                    parseResult.GetValue(heroOption),
                    parseResult.GetValue(attributeOption),
                    parseResult.GetValue(freshOption),
                    parseResult.InvocationConfiguration.Output ?? Console.Out,
                    parseResult.InvocationConfiguration.Error ?? Console.Error,
                    cancellationToken
                )
        );
    }

    /// <summary>1, 2, 3 or 5 for NA, EU, KR or CN (either form); 0 for anything else.</summary>
    public static int Region(string text)
    {
        string value = text?.Trim().ToUpperInvariant();
        return value switch
        {
            "NA" or "1" => 1,
            "EU" or "2" => 2,
            "KR" or "3" => 3,
            "CN" or "5" => 5,
            _ => 0,
        };
    }

    private static async Task<int> RunAsync(
        string battleTag,
        string regionText,
        string hero,
        string attribute,
        bool fresh,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        int region = Region(regionText);
        if (!PlayerHeroStatsFetch.IsBattleTag(battleTag) || region == 0)
        {
            error.WriteLine("--battletag must be Name#1234 and --region one of NA, EU, KR, CN.");
            return 1;
        }

        using ServiceProvider provider = new ServiceCollection()
            .AddTwitchServices(cancellationToken, "heroesreplay-player-hero")
            .AddPlayerHeroStats()
            .BuildHeroesReplayProvider();
        PlayerHeroStatsFetch fetch = provider.GetRequiredService<PlayerHeroStatsFetch>();
        if (fresh)
        {
            fetch.Store.Purge(battleTag, region);
        }

        PlayerHeroLookup lookup = await fetch
            .LookupAsync(battleTag, region, hero, attribute, cancellationToken)
            .ConfigureAwait(false);
        output.WriteLine(Describe(lookup));
        output.WriteLine(
            $"Lookups in the last 7 days: {fetch.Store.CallsThisWeek(DateTimeOffset.UtcNow)}."
        );
        return
            lookup.Status
                is PlayerHeroLookupStatus.Unavailable
                    or PlayerHeroLookupStatus.Refused
                    or PlayerHeroLookupStatus.OverBudget
            ? 1
            : 0;
    }

    public static string Describe(PlayerHeroLookup lookup)
    {
        string source = lookup.Cached ? " (cached)" : string.Empty;
        if (!lookup.IsKnown)
        {
            string reason = string.IsNullOrWhiteSpace(lookup.Reason)
                ? string.Empty
                : ": " + lookup.Reason;
            return $"{lookup.Status}{source}{reason}";
        }

        PlayerHeroStats stats = lookup.Stats;
        string winRate = (stats.WinRate ?? 0).ToString("P1", CultureInfo.InvariantCulture);
        string kda = stats.Kda?.ToString("F2", CultureInfo.InvariantCulture) ?? "-";
        string mmr = stats.HeroMmr?.ToString("F0", CultureInfo.InvariantCulture) ?? "-";
        return $"Known{source}: {stats.Wins} wins of {stats.Games} games ({winRate}), KDA {kda}, hero MMR {mmr}, {stats.RankTier ?? "no tier"}.";
    }
}
