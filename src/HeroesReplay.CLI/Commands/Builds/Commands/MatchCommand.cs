using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Builds.Matching;
using HeroesReplay.Core.Builds.Played;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Builds.Commands;

/// <summary>Prints the talent-build reference's best build for every player of one replay (#412).</summary>
public class MatchCommand : Command
{
    public MatchCommand()
        : base(
            "match",
            "Read one .StormReplay and print, for each player, the talents picked (English names) and the guide build they fit best from the shipped talent-build reference: its score (1 = every pick recommended), how many picks it recommends, the runner-up, and the guide link. Reads the heroes-data2 catalog under Location:DataDirectory. Read-only."
        )
    {
        var replayOption = new Option<string>("--replay")
        {
            Description = "The .StormReplay file.",
            Required = true,
        };
        Options.Add(replayOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(replayOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    cancellationToken
                )
        );
    }

    internal static async Task<int> RunAsync(
        string path,
        TextWriter console,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        console ??= Console.Out;
        error ??= Console.Error;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error.WriteLine($"Replay was not found: {path}");
            return 1;
        }

        (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
            path,
            false,
            ReplayOptions()
        );
        if (replay?.Players == null || replay.Players.Length == 0)
        {
            error.WriteLine($"The replay did not parse: {result}");
            return 1;
        }

        cancellationToken.ThrowIfCancellationRequested();
        AppSettings settings = ServiceCollectionExtensions.LoadOfflineSettings();
        var data = new GameData(NullLogger<GameData>.Instance, settings);
        await data.LoadDataAsync().ConfigureAwait(false);
        Run(replay, data.Heroes, GuideCatalog.Embedded(), new BuildMatcher(), console);
        return 0;
    }

    public static void Run(
        Replay replay,
        IReadOnlyList<Hero> catalog,
        IGuideCatalog guides,
        BuildMatcher matcher,
        TextWriter console
    )
    {
        bool aram = replay.GameMode == GameMode.ARAM;
        foreach (
            Player player in replay
                .Players.Where(player => player != null)
                .OrderBy(player => player.Team)
        )
        {
            string team = player.Team == 0 ? "Blue" : "Red";
            Hero hero = PlayedHero.Find(catalog, replay, player);
            if (hero == null)
            {
                console.WriteLine($"{team} {player.Character}: no catalog hero.");
                continue;
            }

            PlayedBuild played = PlayedBuilds.Read(hero, player);
            string picks = string.Join(
                ", ",
                played.Picks.Select(pick => pick.Name ?? pick.TalentId)
            );
            BuildMatch match = matcher.Match(played, guides.Find(hero), aram);
            console.WriteLine($"{team} {hero.Name}: {picks}");
            console.WriteLine("    " + Describe(match));
        }
    }

    private static string Describe(BuildMatch match)
    {
        if (match == null)
        {
            return "no build: too few talents, or no guide.";
        }

        string best =
            $"{match.Best.Build.Name} ({match.Best.Build.Tag}) {match.Best.Score:F2}, "
            + $"{match.Best.Recommended} of {match.Best.Tiers} picks recommended";
        string next =
            match.RunnerUp == null
                ? string.Empty
                : $"; next {match.RunnerUp.Build.Name} {match.RunnerUp.Score:F2}";
        string ambiguous = match.Ambiguous ? " (ambiguous)" : string.Empty;
        return best + next + ambiguous + " - " + match.Guide.GuideUrl;
    }

    private static ParseOptions ReplayOptions() =>
        new()
        {
            IgnoreErrors = true,
            AllowPTR = true,
            ShouldParseEvents = true,
            ShouldParseUnits = true,
            ShouldParseMouseEvents = false,
            ShouldParseDetailedBattleLobby = false,
            ShouldParseMessageEvents = false,
            ShouldParseStatistics = true,
        };
}
