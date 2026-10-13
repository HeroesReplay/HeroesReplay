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
/// The Storm League map pool (#425). Blizzard rotates maps out of Storm League each season. Two
/// sources decide it:
/// <list type="bullet">
/// <item>Heroes Profile's <c>/maps</c> <c>ranked_rotation</c> flag, saved in
/// <c>Data\HeroesProfile\maps.json</c>. Its 0 always takes a map out.</item>
/// <item>The replays the download role already keeps in <c>Data\Standard</c>. The download takes
/// Heroes Profile's Storm League list with no map filter, so the newest
/// <see cref="SampleSize"/> of them show what is played. A map counts when at least
/// <see cref="MinimumGames"/> of them are on it.</item>
/// </list>
/// A map Heroes Profile flags 1 still needs games in the cached replays: in the 2026-10 season
/// it flagged Hanamura Temple and Warhead Junction, and no Storm League game was played on either.
/// With fewer than <see cref="MinimumSample"/> Storm League replays cached (a new install, a dev
/// box), the flag alone decides. Without either source, the catalog's
/// <c>Maps:Catalog:RankedRotation</c> decides.
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

    /// <summary>True when Heroes Profile's <c>ranked_rotation</c> flags are part of the decision.</summary>
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
        bool? flagged = Flag(key);
        if (flagged == false)
        {
            return false;
        }

        if (pool != null)
        {
            return pool.Contains(key);
        }

        return flagged ?? map.RankedRotation;
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
            (true, true) => $"{flagSource} and the newest {sampled} cached Storm League replays",
            (true, false) => $"{flagSource} (too few cached Storm League replays)",
            (false, true) => $"the newest {sampled} cached Storm League replays",
            _ => "Maps:Catalog RankedRotation (too few cached Storm League replays)",
        };
        string inPool = string.Join(", ", standard.Where(Contains).Select(map => map.Name));
        string outOfPool = string.Join(
            ", ",
            standard.Where(map => !Contains(map)).Select(map => map.Name)
        );
        string line =
            $"Storm League map pool from {source}: {inPool}. Not in Storm League: {(outOfPool.Length == 0 ? "none" : outOfPool)}.";

        // Heroes Profile's flag has lagged a season before (Hanamura Temple, Warhead Junction in
        // 2026-10). Name the maps it calls ranked that no cached Storm League game is on.
        string unplayed = string.Join(
            ", ",
            standard
                .Where(map => Observed && Flag(Key(map.Name)) == true && !Contains(map))
                .Select(map => map.Name)
        );
        return unplayed.Length == 0
            ? line
            : $"{line} Heroes Profile marks {unplayed} ranked, but no cached Storm League replay is on them.";
    }

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
