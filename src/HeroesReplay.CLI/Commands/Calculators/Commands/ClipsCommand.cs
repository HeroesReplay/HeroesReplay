using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Heroes.ReplayParser;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;
using Microsoft.Extensions.Logging.Abstractions;

namespace HeroesReplay.CLI.Commands.Calculators.Commands;

/// <summary>
/// The corpus check behind the kill clip rules: run it before and after a change to a clip rule
/// and compare. It reads the replay rules alone, with no camera check, so it counts the clips a
/// replay could give, not the clips a session cuts.
/// </summary>
public class ClipsCommand : Command
{
    /// <summary>A quadra or triple kill longer than this is listed as slow.</summary>
    private const int SlowSpanSeconds = 20;

    public ClipsCommand()
        : base(
            "clips",
            "Read every .StormReplay in a folder and print the kill clips its replay rules give (pentakill, quadra kill, triple kill), with no camera check. Then the totals: clips by kind, the clips to look at (a victim killed twice in one clip, a clip slower than 20 s), the game's hero deaths against the scoreboard by hero, the deaths whose killing blow the replay does not name by hero, and how often each hero has a position no older than Insights:MaxPositionAge. Run it before and after a change to a clip rule. Read-only."
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

        var parallelOption = new Option<int>("--parallel")
        {
            Description = "Replays read at once. Default 4.",
            DefaultValueFactory = _ => 4,
        };

        var outputOption = new Option<string>("--output")
        {
            Description = "Optional .md report: the totals and one row per clip.",
        };
        outputOption.Aliases.Add("-o");

        Options.Add(directoryOption);
        Options.Add(recursiveOption);
        Options.Add(parallelOption);
        Options.Add(outputOption);

        SetAction(
            (parseResult, cancellationToken) =>
                RunAsync(
                    parseResult.GetValue(directoryOption),
                    parseResult.GetValue(recursiveOption),
                    parseResult.GetValue(parallelOption),
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
        int parallel,
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
        return Run(
            files,
            data.Heroes,
            settings.Insights?.MaxPositionAge ?? TimeSpan.FromSeconds(4),
            Math.Max(1, parallel),
            output,
            console,
            cancellationToken
        );
    }

    /// <summary>Reads every replay and prints its clips and the totals. Exit 0, or 2 when nothing parsed.</summary>
    public static int Run(
        IReadOnlyList<string> files,
        IReadOnlyList<Hero> catalog,
        TimeSpan maxPositionAge,
        int parallel,
        string output,
        TextWriter console,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine($"Reading {files.Count} replays, {parallel} at a time.");
        var results = new ConcurrentBag<ReplayResult>();
        int unread = 0;
        Parallel.ForEach(
            files,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = parallel,
                CancellationToken = cancellationToken,
            },
            file =>
            {
                Replay replay = Parse(file);
                if (replay?.Players == null || replay.Players.Length == 0)
                {
                    Interlocked.Increment(ref unread);
                    return;
                }

                results.Add(
                    Read(
                        Path.GetFileNameWithoutExtension(file).Split('_')[0],
                        replay,
                        catalog,
                        maxPositionAge
                    )
                );
            }
        );

        List<ReplayResult> ordered = results
            .OrderBy(result => result.Id, StringComparer.Ordinal)
            .ToList();
        var text = new StringBuilder();
        foreach (ClipRow clip in ordered.SelectMany(result => result.Clips))
        {
            text.AppendLine(clip.Line);
        }

        text.AppendLine();
        text.Append(Summary(ordered, unread));
        console.Write(text.ToString());
        if (!string.IsNullOrWhiteSpace(output))
        {
            File.WriteAllText(output, "```\n" + text + "```\n");
            console.WriteLine($"Wrote {output}.");
        }

        return ordered.Count == 0 ? 2 : 0;
    }

    private static ReplayResult Read(
        string id,
        Replay replay,
        IReadOnlyList<Hero> catalog,
        TimeSpan maxPositionAge
    )
    {
        HeroPositionFill.Apply(replay);
        Player[] players = replay.Players;
        string[] heroes = players
            .Select(player =>
                player == null ? null : PlayedHero.Name(catalog, replay, player) ?? player.Character
            )
            .ToArray();
        var result = new ReplayResult { Id = id };
        IReadOnlyList<HeroKill> kills = HeroKills.Read(replay);
        result.HasDeathEvents = kills != null;
        foreach (HeroKill kill in kills ?? Array.Empty<HeroKill>())
        {
            string hero = heroes[kill.VictimIndex] ?? "?";
            result.Deaths.Add(hero);
            if (kill.BlowIndex < 0 && kill.KillerIndexes.Count > 0)
            {
                result.UnknownBlows.Add(kill.Body == null ? hero + " (no unit died)" : hero);
            }
        }

        for (int index = 0; index < players.Length; index++)
        {
            if (players[index]?.ScoreResult != null && heroes[index] != null)
            {
                result.ScoreDeaths.Add((heroes[index], players[index].ScoreResult.Deaths));
            }
        }

        foreach (
            TeamKillClip clip in TeamKillClips.Select(TeamKillDeaths.FromReplay(replay, catalog))
        )
        {
            int span = clip.LastDeathSecond - clip.FirstDeathSecond;
            bool repeated =
                clip.Kills.Count != clip.Kills.Select(blow => blow.VictimKey).Distinct().Count();
            string title = TeamKillClips.Title(clip);
            var flags = new List<string>();
            if (repeated)
            {
                flags.Add("REPEATED VICTIM");
            }

            if (clip.Kind == TeamKillClips.TripleKillKind && span > SlowSpanSeconds)
            {
                flags.Add("SLOW");
            }

            result.Clips.Add(
                new ClipRow(
                    title,
                    repeated,
                    flags.Contains("SLOW"),
                    $"{id} | {title} | {clip.Hero} | {Clock(clip.FirstDeathSecond)}-{Clock(clip.LastDeathSecond)} ({span}s) | {string.Join(", ", clip.Kills.Select(blow => blow.Victim))}{(flags.Count == 0 ? string.Empty : " | " + string.Join(", ", flags))}"
                )
            );
        }

        var positions = new HeroPositions(players, maxPositionAge);
        int end = (int)replay.ReplayLength.TotalSeconds;
        for (int index = 0; index < players.Length; index++)
        {
            List<Unit> units = players[index]?.HeroUnits;
            if (units == null || heroes[index] == null)
            {
                continue;
            }

            int alive = 0;
            int placed = 0;
            for (int second = 40; second <= end; second++)
            {
                TimeSpan time = TimeSpan.FromSeconds(second);
                if (
                    !units.Any(unit =>
                        unit != null
                        && time >= unit.TimeSpanBorn
                        && (unit.TimeSpanDied == null || time < unit.TimeSpanDied.Value)
                    )
                )
                {
                    continue;
                }

                alive++;
                if (positions.TryGet(index, time, out _))
                {
                    placed++;
                }
            }

            result.Coverage.Add((heroes[index], alive, placed));
        }

        return result;
    }

    private static string Summary(List<ReplayResult> results, int unread)
    {
        var text = new StringBuilder();
        text.AppendLine(
            $"Replays read: {results.Count}; not read: {unread}; without PlayerDeath events: {results.Count(result => !result.HasDeathEvents)}."
        );
        List<ClipRow> clips = results.SelectMany(result => result.Clips).ToList();
        text.AppendLine();
        text.AppendLine("Kill clips by kind (replay rules, no camera check):");
        foreach (var kind in clips.GroupBy(clip => clip.Title).OrderBy(group => group.Key))
        {
            text.AppendLine(
                $"  {kind.Key}: {kind.Count()} (repeated victim {kind.Count(clip => clip.Repeated)}, slower than {SlowSpanSeconds}s {kind.Count(clip => clip.Slow)})"
            );
        }

        text.AppendLine();
        text.AppendLine("Game hero deaths minus scoreboard deaths, by hero (differences only):");
        var events = results
            .SelectMany(result => result.Deaths)
            .GroupBy(hero => hero)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (
            var hero in results
                .Where(result => result.HasDeathEvents)
                .SelectMany(result => result.ScoreDeaths)
                .GroupBy(row => row.Hero)
                .Select(group => new
                {
                    Hero = group.Key,
                    Games = group.Count(),
                    Difference = events.GetValueOrDefault(group.Key) - group.Sum(row => row.Deaths),
                })
                .Where(row => row.Difference != 0)
                .OrderByDescending(row => Math.Abs(row.Difference))
        )
        {
            text.AppendLine($"  {hero.Hero}: {hero.Difference:+0;-0} over {hero.Games} games");
        }

        int deaths = results.Sum(result => result.Deaths.Count);
        int unknown = results.Sum(result => result.UnknownBlows.Count);
        text.AppendLine();
        text.AppendLine(
            $"Deaths by an enemy whose killing blow the replay does not name: {unknown} of {deaths}."
        );
        foreach (
            var hero in results
                .SelectMany(result => result.UnknownBlows)
                .GroupBy(hero => hero)
                .OrderByDescending(group => group.Count())
                .Take(12)
        )
        {
            text.AppendLine($"  {hero.Key}: {hero.Count()}");
        }

        text.AppendLine();
        text.AppendLine("Living seconds with a position, lowest heroes:");
        var coverage = results
            .SelectMany(result => result.Coverage)
            .GroupBy(row => row.Hero)
            .Select(group => new
            {
                Hero = group.Key,
                Games = group.Count(),
                Alive = group.Sum(row => row.Alive),
                Placed = group.Sum(row => row.Placed),
            })
            .Where(row => row.Alive > 0)
            .ToList();
        long alive = coverage.Sum(row => (long)row.Alive);
        long placed = coverage.Sum(row => (long)row.Placed);
        text.AppendLine($"  all heroes: {(alive == 0 ? 0 : 100.0 * placed / alive):0} %");
        foreach (var hero in coverage.OrderBy(row => (double)row.Placed / row.Alive).Take(10))
        {
            text.AppendLine(
                $"  {hero.Hero}: {100.0 * hero.Placed / hero.Alive:0} % over {hero.Games} games"
            );
        }

        return text.ToString();
    }

    private static string Clock(int second) => $"{second / 60}:{second % 60:00}";

    private static Replay Parse(string file)
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
            return replay;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    private sealed record ClipRow(string Title, bool Repeated, bool Slow, string Line);

    private sealed class ReplayResult
    {
        public string Id { get; init; }

        public bool HasDeathEvents { get; set; }

        public List<ClipRow> Clips { get; } = new();

        public List<string> Deaths { get; } = new();

        public List<string> UnknownBlows { get; } = new();

        public List<(string Hero, int Deaths)> ScoreDeaths { get; } = new();

        public List<(string Hero, int Alive, int Placed)> Coverage { get; } = new();
    }
}
