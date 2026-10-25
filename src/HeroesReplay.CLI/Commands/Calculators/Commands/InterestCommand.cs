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
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile.Interest;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Calculators.Commands;

/// <summary>
/// The corpus report behind the download role's interest pick (#510): each replay scored from
/// its own score screen by the same <see cref="ReplayInterest"/> the pick uses on Heroes
/// Profile's, then how often each signal fires and how the totals spread, so the
/// <c>HeroesProfileApi:Interest</c> thresholds and points can be tuned.
/// </summary>
public class InterestCommand : Command
{
    public InterestCommand()
        : base(
            "interest",
            "Read every .StormReplay in a folder, one file at a time, with events and statistics, and score each from its score screen the way the download role scores Heroes Profile candidates before a download (HeroesProfileApi:Interest): deathless carry, kill leader, long takedown streak, time on fire, team wipe, game length and league. Then how often each signal fires and how the totals spread. A replay is counted once by its id. Reads the heroes-data2 catalog under Location:DataDirectory. Read-only."
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
            .GroupBy(Id, StringComparer.OrdinalIgnoreCase)
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
        return Run(
            files,
            data.Heroes,
            settings.HeroesProfileApi?.Interest ?? new ReplayInterestSettings(),
            output,
            console,
            cancellationToken
        );
    }

    /// <summary>Scores every replay, prints a line each and the frequency tables, and writes <paramref name="output"/>. Exit 0, or 2 when nothing parsed.</summary>
    public static int Run(
        IReadOnlyList<string> files,
        IReadOnlyList<Hero> catalog,
        ReplayInterestSettings interest,
        string output,
        TextWriter console,
        CancellationToken cancellationToken
    )
    {
        var tally = new InterestTally();
        console.WriteLine($"Reading {files.Count} replays, one file at a time.");
        foreach (string file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = Id(file);
            ReplayDetail detail = Parse(file, id, catalog);
            if (detail == null)
            {
                console.WriteLine($"{Path.GetFileName(file)}: not read.");
                continue;
            }

            ReplayInterestScore score = ReplayInterest.Score(
                detail,
                RankImage.RankFromCacheFileName(Path.GetFileName(file)),
                interest
            );
            console.WriteLine(tally.Add(id, score));
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

    /// <summary>The replay id a cache file name starts with, else the name.</summary>
    private static string Id(string path) => Path.GetFileNameWithoutExtension(path).Split('_')[0];

    private static ReplayDetail Parse(string file, string id, IReadOnlyList<Hero> catalog)
    {
        try
        {
            (DataParser.ReplayParseResult _, Replay replay) = DataParser.ParseReplay(
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
            return ReplayDetail.FromReplay(
                int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                    ? value
                    : 0,
                replay,
                catalog
            );
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    private sealed class InterestTally
    {
        private readonly List<(string Id, ReplayInterestScore Score)> rows = new();
        private readonly Dictionary<string, int> signals = new(StringComparer.Ordinal);

        public int Games => rows.Count;

        public string Add(string id, ReplayInterestScore score)
        {
            rows.Add((id, score));
            foreach (string signal in score.Signals.Distinct(StringComparer.Ordinal))
            {
                signals[signal] = signals.GetValueOrDefault(signal) + 1;
            }

            return $"{id}: {score}";
        }

        public string Summary()
        {
            var text = new StringBuilder();
            text.AppendLine($"Games: {Games}");
            text.AppendLine("Signals (games where each fires):");
            foreach (KeyValuePair<string, int> pair in signals.OrderBy(pair => pair.Key))
            {
                text.AppendLine($"  {pair.Key}: {pair.Value} ({Share(pair.Value)})");
            }

            text.AppendLine("Totals:");
            foreach (
                IGrouping<int, (string Id, ReplayInterestScore Score)> group in rows.GroupBy(row =>
                        row.Score.Total
                    )
                    .OrderByDescending(group => group.Key)
            )
            {
                text.AppendLine($"  {group.Key}: {group.Count()} ({Share(group.Count())})");
            }

            return text.ToString().TrimEnd();
        }

        public string Markdown()
        {
            var text = new StringBuilder();
            text.AppendLine("# Replay interest");
            text.AppendLine();
            text.AppendLine("```");
            text.AppendLine(Summary());
            text.AppendLine("```");
            text.AppendLine();
            text.AppendLine("## Replays");
            text.AppendLine();
            text.AppendLine("| Replay | Total | Reasons |");
            text.AppendLine("|---|---|---|");
            foreach (
                (string id, ReplayInterestScore score) in rows.OrderByDescending(row =>
                    row.Score.Total
                )
            )
            {
                text.AppendLine($"| {id} | {score.Total} | {string.Join("; ", score.Reasons)} |");
            }

            return text.ToString();
        }

        private string Share(int count) =>
            Games == 0
                ? "0%"
                : (100.0 * count / Games).ToString("0.0", CultureInfo.InvariantCulture) + "%";
    }
}
