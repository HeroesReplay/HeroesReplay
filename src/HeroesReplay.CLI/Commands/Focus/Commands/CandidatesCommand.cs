using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Builds.Matching;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroFocus;
using HeroesReplay.Core.HeroFocus.Cards;
using HeroesReplay.Core.HeroFocus.Ledger;
using HeroesReplay.Core.HeroFocus.Scoring;
using HeroesReplay.Core.HeroFocus.Signatures;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Focus.Commands;

/// <summary>
/// The corpus harness behind the focus picker (#433): reads a folder of replays into focus cards,
/// builds the hero baselines from them, and ranks every player as a focus with the reason for
/// each score, or the gate that rules them out. <c>--simulate</c> picks focus replays one after
/// another, as the cycle would, to show the variety the novelty terms give.
/// </summary>
public class CandidatesCommand : Command
{
    /// <summary>The time between simulated focus picks: three replays of about 25 minutes.</summary>
    internal static readonly TimeSpan SimulatedSpacing = TimeSpan.FromMinutes(75);

    public CandidatesCommand()
        : base(
            "candidates",
            "Read every .StormReplay in a folder, one at a time, into focus cards (each player's catalog hero, role, score screen, awards, closest guide build and team comps), build the hero baselines from them, and rank every player as a focus: the gates that rule a player out, then quality × (0.5 + novelty) with each part. Heroes Profile is not called, so every player's record counts as unknown. Reads the heroes-data2 catalog under Location:DataDirectory, the shipped talent-build reference and hero signatures, and --ledger when given. Read-only."
        )
    {
        var directoryOption = new Option<string>("--directory")
        {
            Description = "Directory of .StormReplay files.",
            Required = true,
        };
        directoryOption.Aliases.Add("-d");
        var recursiveOption = new Option<bool>("--recursive")
        {
            Description = "Also read the folder's subfolders.",
        };
        var ledgerOption = new Option<string>("--ledger")
        {
            Description =
                "A focus ledger (Data\\Focus\\ledger.json) whose picks count as covered. Default: none, as if nothing had been focused yet.",
        };
        var topOption = new Option<int>("--top")
        {
            Description = "Eligible candidates to print.",
            DefaultValueFactory = _ => 15,
        };
        var simulateOption = new Option<int>("--simulate")
        {
            Description =
                "Pick this many focus replays in a row, each covering its hero for the next, and print the sequence.",
        };
        var anyModeOption = new Option<bool>("--any-mode")
        {
            Description =
                "Score Quick Match and ARAM replays too (the cycle plays Storm League only).",
        };

        Options.Add(directoryOption);
        Options.Add(recursiveOption);
        Options.Add(ledgerOption);
        Options.Add(topOption);
        Options.Add(simulateOption);
        Options.Add(anyModeOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(directoryOption),
                    parseResult.GetValue(recursiveOption),
                    parseResult.GetValue(ledgerOption),
                    parseResult.GetValue(topOption),
                    parseResult.GetValue(simulateOption),
                    parseResult.GetValue(anyModeOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    cancellationToken
                )
        );
    }

    internal static async Task<int> RunAsync(
        string directory,
        bool recursive,
        string ledgerPath,
        int top,
        int simulate,
        bool anyMode,
        TextWriter console,
        TextWriter error,
        CancellationToken cancellationToken
    )
    {
        console ??= Console.Out;
        error ??= Console.Error;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            error.WriteLine($"Replay directory was not found: {directory}");
            return 1;
        }

        if (!string.IsNullOrWhiteSpace(ledgerPath) && !File.Exists(ledgerPath))
        {
            error.WriteLine($"Ledger was not found: {ledgerPath}");
            return 1;
        }

        List<string> files = Directory
            .EnumerateFiles(
                directory,
                "*.StormReplay",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
            )
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            error.WriteLine($"No .StormReplay files were found in {directory}");
            return 1;
        }

        AppSettings settings = ServiceCollectionExtensions.LoadOfflineSettings();
        var data = new GameData(NullLogger<GameData>.Instance, settings);
        await data.LoadDataAsync().ConfigureAwait(false);
        HeroSignatures signatures = HeroSignatures.Embedded();
        var builder = new FocusCardBuilder(
            GuideCatalog.Embedded(),
            new BuildMatcher(
                new BuildMatchOptions
                {
                    MinTiers = settings.Insights.Builds.MinTiers,
                    SituationalWeight = settings.Insights.Builds.SituationalWeight,
                    AmbiguityMargin = settings.Insights.Builds.AmbiguityMargin,
                }
            ),
            settings.YouTube?.Titles
        );
        FocusLedger ledger = string.IsNullOrWhiteSpace(ledgerPath)
            ? new FocusLedger()
            : new FocusLedgerStore(ledgerPath).Read();
        HeroFocusSettings focus = settings.Focus ?? new HeroFocusSettings();

        List<FocusCard> cards = Read(files, data.Heroes, builder, console, cancellationToken);
        if (cards.Count == 0)
        {
            error.WriteLine("No replay in the folder parsed.");
            return 2;
        }

        Run(
            cards,
            new FocusScorer(focus, signatures),
            Baselines(cards, signatures, focus),
            ledger,
            top,
            simulate,
            anyMode,
            DateTime.UtcNow,
            console
        );
        return 0;
    }

    /// <summary>The baselines of the corpus itself: every card's signature stats.</summary>
    public static HeroBaselines Baselines(
        IEnumerable<FocusCard> cards,
        HeroSignatures signatures,
        HeroFocusSettings settings
    )
    {
        var baselines = new HeroBaselines();
        foreach (FocusCard card in cards)
        {
            baselines.Add(card, signatures, settings.BaselineWindow);
        }

        return baselines;
    }

    /// <summary>Prints the gate counts, the best candidates and, with <paramref name="simulate"/>, a run of picks.</summary>
    public static void Run(
        IReadOnlyList<FocusCard> cards,
        FocusScorer scorer,
        HeroBaselines baselines,
        FocusLedger ledger,
        int top,
        int simulate,
        bool anyMode,
        DateTime nowUtc,
        TextWriter console
    )
    {
        IReadOnlyList<FocusCandidate> ranked = scorer.Rank(
            cards,
            ledger,
            baselines,
            nowUtc,
            stormLeagueOnly: !anyMode
        );
        List<FocusCandidate> eligible = ranked.Where(candidate => candidate.Eligible).ToList();
        console.WriteLine(
            $"{cards.Count} cards, {ranked.Count} players, {eligible.Count} eligible as a focus."
        );
        string gates = string.Join(
            ", ",
            ranked
                .Where(candidate => !candidate.Eligible)
                .GroupBy(candidate => candidate.Gate)
                .OrderByDescending(group => group.Count())
                .Select(group => $"{group.Key} {group.Count()}")
        );
        if (gates.Length > 0)
        {
            console.WriteLine($"Ruled out: {gates}.");
        }

        console.WriteLine();
        foreach (FocusCandidate candidate in eligible.Take(Math.Max(0, top)))
        {
            console.WriteLine(Line(candidate));
            console.WriteLine("    " + Parts(candidate));
        }

        if (simulate > 0)
        {
            console.WriteLine();
            Simulate(cards, scorer, baselines, ledger, simulate, anyMode, nowUtc, console);
        }
    }

    /// <summary>
    /// Picks focus replays one after another as the cycle would, 75 minutes apart: each pick
    /// leaves the pool and counts as covered for the next.
    /// </summary>
    public static IReadOnlyList<FocusCandidate> Simulate(
        IReadOnlyList<FocusCard> cards,
        FocusScorer scorer,
        HeroBaselines baselines,
        FocusLedger ledger,
        int picks,
        bool anyMode,
        DateTime nowUtc,
        TextWriter console
    )
    {
        var simulated = new FocusLedger { Entries = (ledger?.Entries ?? new()).ToList() };
        var pool = cards.ToList();
        var chosen = new List<FocusCandidate>();
        console.WriteLine($"Simulating {picks} focus picks:");
        for (int i = 1; i <= picks; i++)
        {
            DateTime at = nowUtc + SimulatedSpacing * (i - 1);
            FocusCandidate best = scorer
                .Rank(pool, simulated, baselines, at, stormLeagueOnly: !anyMode)
                .FirstOrDefault(candidate => candidate.Eligible);
            if (best == null)
            {
                console.WriteLine($"{i, 3}. no eligible candidate is left.");
                break;
            }

            chosen.Add(best);
            simulated.Put(best.ToEntry(FocusEntryState.Played, at));
            pool.RemoveAll(card => string.Equals(card.Key, best.Key, StringComparison.Ordinal));
            console.WriteLine($"{i, 3}. {Line(best)}");
        }

        if (chosen.Count > 0)
        {
            console.WriteLine(
                "Roles: "
                    + string.Join(
                        ", ",
                        chosen
                            .GroupBy(candidate => candidate.Role)
                            .OrderByDescending(group => group.Count())
                            .Select(group => $"{group.Key} {group.Count()}")
                    )
            );
            console.WriteLine(
                $"Heroes: {chosen.Select(candidate => candidate.AttributeId).Distinct().Count()} different in {chosen.Count} picks."
            );
        }

        return chosen;
    }

    /// <summary>One candidate on one line: score, hero, build, role, map, league, region, replay.</summary>
    public static string Line(FocusCandidate candidate) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0:F3}  q {1:F2} n {2:F2}  {3} ({4}), {5} - {6} - {7} - {8} - {9} p{10}",
            candidate.Score,
            candidate.Quality,
            candidate.Novelty,
            candidate.Hero,
            candidate.Build ?? "no build",
            candidate.Role,
            candidate.Map ?? "?",
            candidate.League ?? candidate.Rank ?? "unranked",
            candidate.Region ?? "?",
            candidate.Key,
            candidate.PlayerIndex
        );

    public static string Parts(FocusCandidate candidate)
    {
        string Part(string name) =>
            candidate.Parts.TryGetValue(name, out double value)
                ? value.ToString("F2", CultureInfo.InvariantCulture)
                : "-";

        return $"perf {Part("performance")} (sig {Part("signature")} kda {Part("kda")} part {Part("participation")} awards {Part("awards")} won {Part("won")}) "
            + $"str {Part("strength")} build {Part("build")} game {Part("game")} | "
            + $"hero {Part("novelty.hero")} role {Part("novelty.role")} build {Part("novelty.build")} map {Part("novelty.map")} "
            + $"league {Part("novelty.league")} region {Part("novelty.region")} comp {Part("novelty.comp")}";
    }

    /// <summary>
    /// The card key of a replay file: its Heroes Profile id when the name starts with one
    /// (<c>65550003_StormLeague_Diamond_…</c>), else its name without the extension.
    /// </summary>
    public static FocusReplayFacts Facts(string file)
    {
        string name = Path.GetFileName(file);
        string stem = Path.GetFileNameWithoutExtension(file);
        string first = stem.Split('_')[0];
        int? id =
            int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed > 0
                ? parsed
                : null;
        return new FocusReplayFacts(
            id?.ToString(CultureInfo.InvariantCulture) ?? stem,
            id,
            name,
            RankImage.RankFromCacheFileName(name)
        );
    }

    private static List<FocusCard> Read(
        IReadOnlyList<string> files,
        IReadOnlyList<Hero> catalog,
        FocusCardBuilder builder,
        TextWriter console,
        CancellationToken cancellationToken
    )
    {
        var cards = new List<FocusCard>();
        console.WriteLine($"Reading {files.Count} replays, one file at a time.");
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (DataParser.ReplayParseResult result, Replay replay) = Parse(file);
            if (replay?.Players == null || replay.Players.Length == 0)
            {
                console.WriteLine($"{Path.GetFileName(file)}: not read ({result}).");
                continue;
            }

            cards.Add(builder.Build(replay, catalog, Facts(file), DateTime.UtcNow));
            GC.Collect();
        }

        return cards;
    }

    private static (DataParser.ReplayParseResult, Replay) Parse(string file)
    {
        try
        {
            (DataParser.ReplayParseResult result, Replay replay) = DataParser.ParseReplay(
                file,
                false,
                new ParseOptions
                {
                    IgnoreErrors = true,
                    AllowPTR = true,
                    ShouldParseEvents = true,
                    ShouldParseUnits = true,
                    ShouldParseStatistics = true,
                    ShouldParseDetailedBattleLobby = true,
                    ShouldParseMouseEvents = false,
                    ShouldParseMessageEvents = false,
                }
            );
            return (result, replay);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return (DataParser.ReplayParseResult.Exception, null);
        }
    }
}
