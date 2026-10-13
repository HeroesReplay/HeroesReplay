using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
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
/// The Storm League map pool, read from the replays the download role already keeps in
/// <c>Data\Standard</c> (#425). Blizzard rotates maps out of Storm League each season, and the
/// download takes Heroes Profile's Storm League list with no map filter, so the newest
/// <see cref="SampleSize"/> of them show the pool. A map counts when at least
/// <see cref="MinimumGames"/> of them are on it. With fewer than <see cref="MinimumSample"/>
/// Storm League replays cached (a new install, a dev box), the catalog's
/// <c>Maps:Catalog:RankedRotation</c> decides instead.
/// </summary>
public sealed class StormLeagueRotation : IStormLeagueRotation
{
    public const int SampleSize = 300;
    public const int MinimumSample = 100;
    public const int MinimumGames = 3;

    private const string StormLeague = "Storm League";

    private readonly HashSet<string> pool;
    private readonly int sampled;

    private StormLeagueRotation(HashSet<string> pool, int sampled)
    {
        this.pool = pool;
        this.sampled = sampled;
    }

    /// <summary>The catalog's <c>RankedRotation</c> flags, with no cached replays read.</summary>
    public static StormLeagueRotation Catalog { get; } = new(null, 0);

    /// <summary>True when the pool came from cached replays, not the catalog.</summary>
    public bool Observed => pool != null;

    public static StormLeagueRotation FromCache(AppSettings settings)
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

        return pool == null ? map.RankedRotation : pool.Contains(Key(map.Name));
    }

    public string Describe(IEnumerable<Map> catalog)
    {
        List<Map> standard = (catalog ?? Enumerable.Empty<Map>())
            .Where(map => map != null && map.Playable && map.Type == "standard")
            .ToList();
        string source = Observed
            ? $"the newest {sampled} cached Storm League replays"
            : "Maps:Catalog RankedRotation (too few cached Storm League replays)";
        string inPool = string.Join(", ", standard.Where(Contains).Select(map => map.Name));
        string outOfPool = string.Join(
            ", ",
            standard.Where(map => !Contains(map)).Select(map => map.Name)
        );
        return $"Storm League map pool from {source}: {inPool}. Not in Storm League: {(outOfPool.Length == 0 ? "none" : outOfPool)}.";
    }

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
