using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.YouTube.Playlists;

namespace HeroesReplay.Core.HeroFocus.Ledger;

/// <summary>How many focus picks one hero, role, map, league, region or build has had.</summary>
public sealed record FocusCoverageCount(string Name, int Picks, int Published, double Share);

/// <summary>
/// What the focus picks (#433) have covered so far: every hero of the roster, covered or not,
/// and the spread over roles, maps, leagues, regions and builds.
/// </summary>
public sealed record FocusCoverageReport
{
    public int Picks { get; init; }
    public int Played { get; init; }
    public int Published { get; init; }
    public int RosterSize { get; init; }
    public int HeroesCovered { get; init; }
    public IReadOnlyList<string> Uncovered { get; init; } = Array.Empty<string>();
    public IReadOnlyList<FocusCoverageCount> Heroes { get; init; } =
        Array.Empty<FocusCoverageCount>();
    public IReadOnlyList<FocusCoverageCount> Roles { get; init; } =
        Array.Empty<FocusCoverageCount>();
    public IReadOnlyList<FocusCoverageCount> Maps { get; init; } =
        Array.Empty<FocusCoverageCount>();
    public IReadOnlyList<FocusCoverageCount> Leagues { get; init; } =
        Array.Empty<FocusCoverageCount>();
    public IReadOnlyList<FocusCoverageCount> Regions { get; init; } =
        Array.Empty<FocusCoverageCount>();
    public IReadOnlyList<FocusCoverageCount> Builds { get; init; } =
        Array.Empty<FocusCoverageCount>();
}

public static class FocusCoverage
{
    /// <summary>
    /// The coverage of the ledger's Picked and Played entries. <paramref name="roster"/> is every
    /// hero name, for the uncovered list. <paramref name="published"/> holds the replay ids on
    /// YouTube (<see cref="PublishedReplayIds"/>).
    /// </summary>
    public static FocusCoverageReport Build(
        FocusLedger ledger,
        IEnumerable<string> roster,
        IReadOnlySet<int> published = null
    )
    {
        IReadOnlyList<FocusLedgerEntry> history = (ledger ?? new FocusLedger()).History;
        published ??= new HashSet<int>();
        List<string> heroes = (roster ?? Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var covered = new HashSet<string>(
            history.Select(entry => entry.Hero).Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase
        );

        return new FocusCoverageReport
        {
            Picks = history.Count,
            Played = history.Count(entry => entry.State == FocusEntryState.Played),
            Published = history.Count(entry => IsPublished(entry, published)),
            RosterSize = heroes.Count,
            HeroesCovered = heroes.Count(covered.Contains),
            Uncovered = heroes.Where(name => !covered.Contains(name)).ToList(),
            Heroes = Count(history, entry => entry.Hero, published),
            Roles = Count(history, entry => entry.Role, published),
            Maps = Count(history, entry => entry.Map, published),
            Leagues = Count(history, entry => entry.League, published),
            Regions = Count(history, entry => entry.Region, published),
            Builds = Count(
                history,
                entry => entry.Build == null ? null : entry.Hero + ": " + entry.Build,
                published
            ),
        };
    }

    /// <summary>The replay ids of the full-match videos in <c>Data\youtube-library.jsonl</c>.</summary>
    public static IReadOnlySet<int> PublishedReplayIds(string libraryPath) =>
        YouTubeLibraryRecord
            .Read(libraryPath)
            .Values.Where(video => !video.IsClip && video.ReplayId is > 0)
            .Select(video => video.ReplayId.Value)
            .ToHashSet();

    private static bool IsPublished(FocusLedgerEntry entry, IReadOnlySet<int> published) =>
        entry.ReplayId is int id && published.Contains(id);

    private static IReadOnlyList<FocusCoverageCount> Count(
        IReadOnlyList<FocusLedgerEntry> history,
        Func<FocusLedgerEntry, string> name,
        IReadOnlySet<int> published
    )
    {
        var named = history
            .Select(entry => (Entry: entry, Name: name(entry)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToList();
        return named
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new FocusCoverageCount(
                group.Key,
                group.Count(),
                group.Count(item => IsPublished(item.Entry, published)),
                named.Count == 0 ? 0 : Math.Round((double)group.Count() / named.Count, 3)
            ))
            .OrderByDescending(count => count.Picks)
            .ThenBy(count => count.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
