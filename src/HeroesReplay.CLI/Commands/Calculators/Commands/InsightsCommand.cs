using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Builds;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.Analysis.Insights.Players;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Calculators.Commands;

/// <summary>
/// The corpus report behind the match insights (#412): per replay, the solo laners, standouts,
/// MVP, camps, buildings lost, core race, comeback and guide builds; then how often each appears,
/// so the <c>Insights</c> thresholds can be tuned on a fresh corpus.
/// </summary>
public class InsightsCommand : Command
{
    public InsightsCommand()
        : base(
            "insights",
            "Read every .StormReplay in a folder, one file at a time, with units, events and statistics, and print each replay's match insights: each team's solo laner and its confidence, positive standouts, the MVP, camps (and how many on the enemy side), forts and keeps lost, a core race, a comeback, and each player's closest guide build. Then how often each appears per game, per team or per player, to tune the Insights thresholds. Reads the heroes-data2 catalog under Location:DataDirectory and the shipped talent-build reference. Read-only."
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

        var outputOption = new Option<string>("--output")
        {
            Description = "Optional .md report: the frequency tables and one row per replay.",
        };
        outputOption.Aliases.Add("-o");

        Options.Add(directoryOption);
        Options.Add(recursiveOption);
        Options.Add(outputOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(directoryOption),
                    parseResult.GetValue(recursiveOption),
                    parseResult.GetValue(outputOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    cancellationToken
                )
        );
    }

    internal static async Task<int> RunAsync(
        string directory,
        bool recursive,
        string output,
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

        if (
            !string.IsNullOrWhiteSpace(output)
            && !output.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        )
        {
            error.WriteLine("--output must end in .md.");
            return 1;
        }

        List<string> files = Directory
            .EnumerateFiles(
                directory,
                "*.StormReplay",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly
            )
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
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
        IMatchInsightsReader reader = MatchInsightsReader.Create(
            settings.Insights,
            GuideCatalog.Embedded()
        );
        return Run(files, data.Heroes, reader, output, console, cancellationToken);
    }

    /// <summary>Reads every replay, prints a line each and the frequency tables, and writes <paramref name="output"/>. Exit 0, or 2 when nothing parsed.</summary>
    public static int Run(
        IReadOnlyList<string> files,
        IReadOnlyList<Hero> catalog,
        IMatchInsightsReader reader,
        string output,
        TextWriter console,
        CancellationToken cancellationToken
    )
    {
        var tally = new InsightsTally();
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

            MatchInsights insights = reader.Read(replay, catalog);
            string row = tally.Add(Path.GetFileNameWithoutExtension(file).Split('_')[0], insights);
            console.WriteLine(row);
            GC.Collect();
        }

        console.WriteLine();
        console.WriteLine(tally.Summary());
        if (!string.IsNullOrWhiteSpace(output))
        {
            File.WriteAllText(output, tally.Markdown());
            console.WriteLine($"Wrote {output}.");
        }

        return tally.Games == 0 ? 2 : 0;
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
                    ShouldParseMouseEvents = false,
                    ShouldParseDetailedBattleLobby = false,
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

    private sealed class InsightsTally
    {
        private readonly List<string> rows = new();
        private readonly Dictionary<string, int> games = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> teams = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> players = new(StringComparer.Ordinal);
        private int teamCount;
        private int playerCount;

        public int Games { get; private set; }

        public string Add(string id, MatchInsights insights)
        {
            Games++;
            var parts = new List<string> { id };
            foreach (TeamLanes lanes in insights.Lanes.Teams)
            {
                teamCount++;
                Count(teams, "solo laner " + lanes.Confidence);
                parts.Add(
                    $"{Team(lanes.Team)} solo {lanes.SoloLaner?.Hero ?? "-"} ({lanes.Confidence})"
                );
            }

            foreach (
                StandoutKind kind in insights
                    .Players.Standouts.Select(standout => standout.Kind)
                    .Distinct()
            )
            {
                Count(games, "standout " + kind);
            }

            if (insights.Players.Standouts.Count > 0)
            {
                Count(games, "any standout");
                parts.Add(
                    string.Join(
                        "; ",
                        insights.Players.Standouts.Select(standout =>
                            $"{standout.Kind} {standout.Player.Hero} {standout.Player.Kda}"
                        )
                    )
                );
            }

            if (insights.Players.Mvp != null)
            {
                Count(games, "MVP award present");
                parts.Add($"MVP {insights.Players.Mvp.Hero}");
            }

            if (insights.Camps.Teams.Any(team => team.Drought))
            {
                Count(games, "camp drought");
            }

            if (insights.Camps.Teams.Count == 2)
            {
                parts.Add(
                    $"camps B {insights.Camps.Teams[0].Captures} ({insights.Camps.Teams[0].EnemySideCaptures}) R {insights.Camps.Teams[1].Captures} ({insights.Camps.Teams[1].EnemySideCaptures})"
                );
            }

            if (insights.Finish.CoreRace)
            {
                Count(games, "core race");
                parts.Add("core race");
            }

            for (
                int deficit = 1;
                deficit <= Math.Min(insights.Comeback.BuildingDeficit, 6);
                deficit++
            )
            {
                Count(games, "comeback buildings >= " + deficit);
            }

            for (int levels = 1; levels <= Math.Min(insights.Comeback.MaxLevelDeficit, 4); levels++)
            {
                Count(games, "comeback levels >= " + levels);
            }
            if (insights.Comeback.Comeback)
            {
                Count(games, "comeback");
                parts.Add(
                    $"comeback (buildings {insights.Comeback.BuildingDeficit}, levels {insights.Comeback.MaxLevelDeficit})"
                );
            }

            foreach (PlayerBuild build in insights.Builds.Players)
            {
                playerCount++;
                Count(players, "build " + Bucket(build));
            }

            if (insights.Focus.Top != null)
            {
                parts.Add($"most followed {insights.Focus.Top.Hero}");
            }

            string row = string.Join(" | ", parts);
            rows.Add(row);
            return row;
        }

        public string Summary()
        {
            var text = new StringBuilder();
            text.AppendLine(
                $"{Games} games, {teamCount} teams, {playerCount} players with a guide build."
            );
            Append(text, "Per game", games, Games);
            Append(text, "Per team", teams, teamCount);
            Append(text, "Per player", players, playerCount);
            return text.ToString();
        }

        public string Markdown()
        {
            var text = new StringBuilder();
            text.AppendLine("# Match insights corpus report (#412)");
            text.AppendLine();
            text.AppendLine(
                "Comeback counts are cumulative: `comeback buildings >= 3` counts games whose surviving team lost 3 or more buildings more than the other."
            );
            text.AppendLine();
            text.AppendLine("```");
            text.Append(Summary());
            text.AppendLine("```");
            text.AppendLine();
            text.AppendLine("## Replays");
            text.AppendLine();
            foreach (string row in rows)
            {
                text.AppendLine("- " + row);
            }

            return text.ToString();
        }

        private static void Append(
            StringBuilder text,
            string title,
            Dictionary<string, int> counts,
            int total
        )
        {
            text.AppendLine(title + ":");
            foreach (
                KeyValuePair<string, int> count in counts.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal
                )
            )
            {
                double share = total == 0 ? 0 : count.Value * 100.0 / total;
                text.AppendLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"  {count.Key}: {count.Value} ({share:F0}%)"
                    )
                );
            }
        }

        private static string Bucket(PlayerBuild build) =>
            build.Score >= 0.85 ? "strong (>= 0.85)"
            : build.Score >= 0.70 ? "good (0.70-0.85)"
            : build.Score >= 0.50 ? "partial (0.50-0.70)"
            : "off-guide (< 0.50)";

        private static string Team(int team) => team == 0 ? "Blue" : "Red";

        private static void Count(Dictionary<string, int> counts, string key) =>
            counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
