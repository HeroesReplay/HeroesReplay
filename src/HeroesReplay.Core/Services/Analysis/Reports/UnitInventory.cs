using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Services.Analysis.Reports;

public sealed class UnitInventoryRow
{
    public string Map { get; set; }

    public string Name { get; set; }

    public string ParserGroup { get; set; }

    public int Occurrences { get; set; }

    public int ReplaysWithUnit { get; set; }

    public int ReplaysSampled { get; set; }

    public string ReplayVersions { get; set; }
}

public sealed class UnitInventory
{
    private readonly Dictionary<string, MutableRow> rows = new Dictionary<string, MutableRow>(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, int> sampled = new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase
    );

    public void AddReplay(
        string map,
        string version,
        IEnumerable<(string Name, string Group)> units
    )
    {
        string mapKey = string.IsNullOrWhiteSpace(map) ? "(unknown)" : map.Trim();
        string versionKey = string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim();
        int alreadySampled;
        sampled.TryGetValue(mapKey, out alreadySampled);
        sampled[mapKey] = alreadySampled + 1;

        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string Name, string Group) unit in units ?? Enumerable.Empty<(string, string)>())
        {
            if (string.IsNullOrWhiteSpace(unit.Name))
            {
                continue;
            }

            string key = mapKey + "\n" + unit.Name;
            MutableRow row;
            if (!rows.TryGetValue(key, out row))
            {
                row = new MutableRow { Map = mapKey, Name = unit.Name };
                rows.Add(key, row);
            }

            row.Occurrences++;
            row.NoteGroup(string.IsNullOrWhiteSpace(unit.Group) ? "Unknown" : unit.Group);
            row.Versions.Add(versionKey);
            if (distinct.Add(unit.Name))
            {
                row.ReplaysWithUnit++;
            }
        }
    }

    public IReadOnlyList<UnitInventoryRow> Rows()
    {
        var result = new List<UnitInventoryRow>();
        foreach (MutableRow row in rows.Values)
        {
            int replaysSampled;
            sampled.TryGetValue(row.Map, out replaysSampled);
            result.Add(
                new UnitInventoryRow
                {
                    Map = row.Map,
                    Name = row.Name,
                    ParserGroup = row.MajorityGroup(),
                    Occurrences = row.Occurrences,
                    ReplaysWithUnit = row.ReplaysWithUnit,
                    ReplaysSampled = replaysSampled,
                    ReplayVersions = string.Join(
                        ";",
                        row.Versions.OrderBy(version => version, StringComparer.Ordinal)
                    ),
                }
            );
        }

        return result
            .OrderBy(row => row.Map, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed class MutableRow
    {
        private readonly Dictionary<string, int> groups = new Dictionary<string, int>(
            StringComparer.Ordinal
        );

        public string Map { get; set; }

        public string Name { get; set; }

        public int Occurrences { get; set; }

        public int ReplaysWithUnit { get; set; }

        public HashSet<string> Versions { get; } = new HashSet<string>(StringComparer.Ordinal);

        public void NoteGroup(string group)
        {
            int count;
            groups.TryGetValue(group, out count);
            groups[group] = count + 1;
        }

        public string MajorityGroup()
        {
            string best = "Unknown";
            int bestCount = -1;
            foreach (
                KeyValuePair<string, int> pair in groups.OrderBy(
                    pair => pair.Key,
                    StringComparer.Ordinal
                )
            )
            {
                if (pair.Value > bestCount)
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }

            return best;
        }
    }
}
