using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// Heroes Profile hero statistics for one major patch (<c>2.57</c>) and one game type
/// (<c>sl</c>), as the download role saved them. Heroes are keyed by <c>attribute_id</c>, which
/// is the heroes-data2 <c>AttributeId</c>. Every rate is computed from wins and games.
/// </summary>
public sealed class HeroStatsSnapshot
{
    private Dictionary<string, HeroStats> byAttribute;

    public string Patch { get; set; }
    public string GameType { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
    public List<HeroStats> Heroes { get; set; } = new List<HeroStats>();

    /// <summary>The hero with this <c>attribute_id</c>, or null.</summary>
    public HeroStats Find(string attributeId)
    {
        if (string.IsNullOrWhiteSpace(attributeId))
        {
            return null;
        }

        if (byAttribute == null)
        {
            var index = new Dictionary<string, HeroStats>(StringComparer.OrdinalIgnoreCase);
            foreach (HeroStats hero in Heroes ?? new List<HeroStats>())
            {
                if (hero != null && !string.IsNullOrWhiteSpace(hero.AttributeId))
                {
                    index.TryAdd(hero.AttributeId.Trim(), hero);
                }
            }

            byAttribute = index;
        }

        return byAttribute.TryGetValue(attributeId.Trim(), out HeroStats found) ? found : null;
    }
}

/// <summary>One hero: its record across every map, per map, and against and with each other hero.</summary>
public sealed class HeroStats
{
    public string AttributeId { get; set; }
    public string Name { get; set; }

    /// <summary>The hero's wins over every map of the patch.</summary>
    public int Wins { get; set; }

    /// <summary>The hero's games over every map of the patch.</summary>
    public int Games { get; set; }

    public List<HeroMapStats> Maps { get; set; } = new List<HeroMapStats>();

    /// <summary>This hero's wins and games against each enemy hero.</summary>
    public List<HeroPairStats> Enemies { get; set; } = new List<HeroPairStats>();

    /// <summary>This hero's wins and games with each allied hero.</summary>
    public List<HeroPairStats> Allies { get; set; } = new List<HeroPairStats>();

    [JsonIgnore]
    public double? WinRate => HeroStatsMath.Rate(Wins, Games);

    public HeroMapStats Map(string map)
    {
        if (string.IsNullOrWhiteSpace(map) || Maps == null)
        {
            return null;
        }

        foreach (HeroMapStats row in Maps)
        {
            if (
                row != null
                && string.Equals(row.Map?.Trim(), map.Trim(), StringComparison.OrdinalIgnoreCase)
            )
            {
                return row;
            }
        }

        return null;
    }

    public HeroPairStats Enemy(string attributeId) => Pair(Enemies, attributeId);

    public HeroPairStats Ally(string attributeId) => Pair(Allies, attributeId);

    private static HeroPairStats Pair(List<HeroPairStats> pairs, string attributeId)
    {
        if (pairs == null || string.IsNullOrWhiteSpace(attributeId))
        {
            return null;
        }

        foreach (HeroPairStats pair in pairs)
        {
            if (
                pair != null
                && string.Equals(
                    pair.AttributeId?.Trim(),
                    attributeId.Trim(),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return pair;
            }
        }

        return null;
    }
}

/// <summary>One hero on one map: wins, games, and the share of that map's games it was banned in.</summary>
public sealed class HeroMapStats
{
    public string Map { get; set; }
    public int Wins { get; set; }
    public int Games { get; set; }

    /// <summary>Percent, 0 to 100.</summary>
    public double BanRate { get; set; }

    [JsonIgnore]
    public double? WinRate => HeroStatsMath.Rate(Wins, Games);
}

/// <summary>The owning hero's wins and games against, or with, the hero named here.</summary>
public sealed class HeroPairStats
{
    public string AttributeId { get; set; }
    public int Wins { get; set; }
    public int Games { get; set; }

    [JsonIgnore]
    public double? WinRate => HeroStatsMath.Rate(Wins, Games);
}

public static class HeroStatsMath
{
    /// <summary>Percent won, or null without games.</summary>
    public static double? Rate(int wins, int games) =>
        games <= 0 ? null : 100.0 * Math.Clamp(wins, 0, games) / games;

    /// <summary>
    /// The low end of the 95% Wilson score interval for <paramref name="wins"/> of
    /// <paramref name="games"/>, in percent. Zero without games.
    /// </summary>
    public static double WilsonLower(int wins, int games)
    {
        if (games <= 0)
        {
            return 0;
        }

        const double z = 1.96;
        double n = games;
        double p = Math.Clamp(wins, 0, games) / n;
        double z2 = z * z;
        double centre = p + z2 / (2 * n);
        double margin = z * Math.Sqrt(p * (1 - p) / n + z2 / (4 * n * n));
        return 100.0 * (centre - margin) / (1 + z2 / n);
    }

    /// <summary>
    /// What <c>A</c> should win against <c>B</c> from their overall rates alone:
    /// <c>50 + (A - 50) - (B - 50)</c>, in percent.
    /// </summary>
    public static double ExpectedAgainst(double rateA, double rateB) =>
        Math.Clamp(50 + (rateA - 50) - (rateB - 50), 0, 100);
}
