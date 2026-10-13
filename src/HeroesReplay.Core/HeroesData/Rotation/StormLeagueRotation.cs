using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.HeroesData.Rotation;

/// <summary>Which maps are in this season's Storm League map pool (#425).</summary>
public interface IStormLeagueRotation
{
    /// <summary>True when <paramref name="map"/> is in the Storm League map pool.</summary>
    bool Contains(Map map);

    /// <summary>One line for the log: where the pool came from and what it holds.</summary>
    string Describe(IEnumerable<Map> catalog);
}

/// <summary>
/// The Storm League map pool (#425). Blizzard rotates maps out of Storm League each season.
/// <list type="bullet">
/// <item>The replays the download role already keeps in <c>Data\Standard</c> decide it. The
/// download takes Heroes Profile's Storm League list with no map filter, so the newest
/// <see cref="SampleSize"/> of them show what is played. A map counts when at least
/// <see cref="MinimumGames"/> of them are on it.</item>
/// <item>With fewer than <see cref="MinimumSample"/> Storm League replays cached (a new install,
/// a dev box), the catalog's <c>Maps:Catalog:RankedRotation</c> decides, less any map Heroes
/// Profile's <c>/maps</c> <c>ranked_rotation</c> (<c>Data\HeroesProfile\maps.json</c>) marks
/// 0.</item>
/// </list>
/// Heroes Profile's flag never adds a map, and never overrides the cached games: it lagged a
/// season. On 2026-10-13 it marked Hanamura Temple and Warhead Junction ranked, and Heroes
/// Profile's own replays have no Storm League game on either since build 2.55.17.97605 (20 Jul
/// 2026). <see cref="Describe"/> names every map where it disagrees.
/// </summary>
public sealed class StormLeagueRotation : IStormLeagueRotation
{
    public const int SampleSize = 300;
    public const int MinimumSample = 100;
    public const int MinimumGames = 3;

    private const string StormLeague = "Storm League";

    private readonly HashSet<string> pool;
    private readonly int sampled;
    private readonly HeroesProfileMapList heroesProfile;
    private readonly Dictionary<string, bool> flags;

    private StormLeagueRotation(
        HashSet<string> pool,
        int sampled,
        HeroesProfileMapList heroesProfile = null
    )
    {
        this.pool = pool;
        this.sampled = sampled;
        this.heroesProfile = heroesProfile;
        if (heroesProfile != null)
        {
            flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (HeroesProfileMap map in heroesProfile.Maps)
            {
                string key = Key(map.Name);
                if (key != null)
                {
                    flags[key] = map.RankedRotation;
                }
            }
        }
    }

    /// <summary>The catalog's <c>RankedRotation</c> flags, with no cached replays read.</summary>
    public static StormLeagueRotation Catalog { get; } = new(null, 0);

    /// <summary>True when the pool came from cached replays, not the catalog.</summary>
    public bool Observed => pool != null;

    /// <summary>True when Heroes Profile's <c>ranked_rotation</c> flags were read.</summary>
    public bool Flagged => flags != null;

    /// <summary>
    /// The pool from the cached replays and the saved Heroes Profile map list. It reads files
    /// only; <see cref="HeroesProfileMapRefresh"/> fetches the list before a reward sync.
    /// </summary>
    public static StormLeagueRotation FromCache(AppSettings settings) =>
        FromReplayCache(settings).With(HeroesProfileMapStore.For(settings).Read());

    /// <summary>This pool with Heroes Profile's flags from <paramref name="maps"/> (null: none).</summary>
    public StormLeagueRotation With(HeroesProfileMapList maps) => new(pool, sampled, maps);

    private static StormLeagueRotation FromReplayCache(AppSettings settings)
    {
        string directory =
            string.IsNullOrWhiteSpace(settings?.Location?.DataDirectory)
            || string.IsNullOrWhiteSpace(settings.HeroesProfileApi?.StandardCacheDirectoryName)
                ? null
                : settings.StandardReplayCachePath;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return Catalog;
        }

        try
        {
            return FromFileNames(
                Directory.EnumerateFiles(directory, "*.StormReplay").Select(Path.GetFileName)
            );
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Catalog;
        }
    }

    /// <summary>
    /// The pool from cache file names, <c>{id}_{game type}_{rank}_{map}_{fingerprint}_.StormReplay</c>.
    /// </summary>
    public static StormLeagueRotation FromFileNames(IEnumerable<string> fileNames)
    {
        List<(int Id, string Map)> newest = (fileNames ?? Enumerable.Empty<string>())
            .Select(TryRead)
            .Where(replay => replay.HasValue)
            .Select(replay => replay.Value)
            .OrderByDescending(replay => replay.Id)
            .Take(SampleSize)
            .ToList();
        if (newest.Count < MinimumSample)
        {
            return Catalog;
        }

        var pool = new HashSet<string>(
            newest
                .GroupBy(replay => replay.Map, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() >= MinimumGames)
                .Select(group => group.Key),
            StringComparer.OrdinalIgnoreCase
        );
        return new StormLeagueRotation(pool, newest.Count);
    }

    public bool Contains(Map map)
    {
        if (map == null)
        {
            return false;
        }

        string key = Key(map.Name);
        if (pool != null)
        {
            return pool.Contains(key);
        }

        return map.RankedRotation && Flag(key) != false;
    }

    public string Describe(IEnumerable<Map> catalog)
    {
        List<Map> standard = (catalog ?? Enumerable.Empty<Map>())
            .Where(map => map != null && map.Playable && map.Type == "standard")
            .ToList();
        string flagSource = Flagged
            ? $"Heroes Profile ranked_rotation ({heroesProfile.FetchedAtUtc:yyyy-MM-dd})"
            : null;
        string source = (Flagged, Observed) switch
        {
            (_, true) => $"the newest {sampled} cached Storm League replays",
            (true, false) =>
                $"Maps:Catalog RankedRotation less the maps {flagSource} marks 0 (too few cached Storm League replays)",
            _ => "Maps:Catalog RankedRotation (too few cached Storm League replays)",
        };
        string inPool = Names(standard.Where(Contains));
        string outOfPool = Names(standard.Where(map => !Contains(map)));
        string line =
            $"Storm League map pool from {source}: {(inPool.Length == 0 ? "none" : inPool)}. Not in Storm League: {(outOfPool.Length == 0 ? "none" : outOfPool)}.";

        // Heroes Profile's flag lagged a season (Hanamura Temple, Warhead Junction in 2026-10), so
        // it only takes part as a check. Name every map where it disagrees with the pool.
        string ranked = Names(standard.Where(map => Flag(Key(map.Name)) == true && !Contains(map)));
        string unranked = Names(
            standard.Where(map => Flag(Key(map.Name)) == false && Contains(map))
        );
        if (ranked.Length > 0)
        {
            line += Observed
                ? $" Heroes Profile marks {ranked} ranked, but no cached Storm League replay is on them."
                : $" Heroes Profile marks {ranked} ranked, but Maps:Catalog does not.";
        }

        if (unranked.Length > 0)
        {
            line +=
                $" Heroes Profile marks {unranked} not ranked, but cached Storm League replays are on them.";
        }

        return line;
    }

    private static string Names(IEnumerable<Map> maps) =>
        string.Join(", ", maps.Select(map => map.Name));

    private bool? Flag(string key) =>
        flags != null && key != null && flags.TryGetValue(key, out bool ranked) ? ranked : null;

    private static (int Id, string Map)? TryRead(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string[] parts = fileName.Split('_');
        if (
            parts.Length < 4
            || !int.TryParse(parts[0], out int id)
            || !string.Equals(parts[1], StormLeague, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parts[3])
        )
        {
            return null;
        }

        return (id, Key(parts[3]));
    }

    private static string Key(string map) => EnglishMapNames.Canonical(map) ?? map?.Trim();
}
