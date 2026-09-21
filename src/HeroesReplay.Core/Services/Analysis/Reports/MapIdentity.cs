using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Services.Analysis.Reports;

public static class MapIdentity
{
    public static bool CanIdentifyMap(string name, string group)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.StartsWith("Hero", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (name.StartsWith("Town", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (
            group == "Minions"
            || group == "Hero"
            || group == "HeroAbilityUse"
            || group == "HeroTalentSelection"
        )
        {
            return false;
        }

        if (
            name.Contains("RegenGlobe", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ExperienceGlobe", StringComparison.OrdinalIgnoreCase)
            || name.Contains("PathingBlocker", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dummy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Icon", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        return group == "MapObjective"
            || group == "MercenaryCamp"
            || group == "Miscellaneous"
            || group == "Unknown";
    }

    public static IReadOnlyDictionary<string, string> UniqueMarkers(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> unitsByMap
    )
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (
            KeyValuePair<string, IReadOnlyCollection<string>> map in unitsByMap
                ?? new Dictionary<string, IReadOnlyCollection<string>>()
        )
        {
            if (map.Value == null)
            {
                continue;
            }

            foreach (string name in map.Value.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                List<string> maps;
                if (!owners.TryGetValue(name, out maps))
                {
                    maps = new List<string>();
                    owners.Add(name, maps);
                }

                if (!maps.Contains(map.Key, StringComparer.OrdinalIgnoreCase))
                {
                    maps.Add(map.Key);
                }
            }
        }

        var markers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, List<string>> pair in owners)
        {
            if (pair.Value.Count == 1)
            {
                markers[pair.Key] = pair.Value[0];
            }
        }

        return markers;
    }

    public static string Match(
        IEnumerable<string> unitNames,
        IReadOnlyDictionary<string, string> markers,
        string fallback
    )
    {
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (unitNames != null && markers != null)
        {
            foreach (string name in unitNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string map;
                if (!markers.TryGetValue(name, out map))
                {
                    continue;
                }

                int hits;
                scores.TryGetValue(map, out hits);
                scores[map] = hits + 1;
            }
        }

        if (scores.Count == 0)
        {
            return fallback;
        }

        List<KeyValuePair<string, int>> ranked = scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ranked[0].Value < 2)
        {
            return fallback;
        }

        if (ranked.Count > 1 && ranked[0].Value == ranked[1].Value)
        {
            return fallback;
        }

        return ranked[0].Key;
    }
}
