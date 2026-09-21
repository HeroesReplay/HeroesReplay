using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.Services.Analysis.Reports;

public sealed class MapReplaySample
{
    public MapReplaySample(string map, IReadOnlyList<string> paths, int filesIdentified)
    {
        Map = map;
        Paths = paths;
        FilesIdentified = filesIdentified;
    }

    public string Map { get; }

    public IReadOnlyList<string> Paths { get; }

    public int FilesIdentified { get; }
}

/// <summary>
/// Keeps the first replays seen for each map. Callers must feed files one at a time.
/// </summary>
public sealed class MapReplaySampler
{
    private readonly int perMap;
    private readonly Dictionary<string, List<string>> selected = new Dictionary<
        string,
        List<string>
    >(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> identified = new Dictionary<string, int>(
        StringComparer.OrdinalIgnoreCase
    );

    public MapReplaySampler(int perMap)
    {
        if (perMap < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(perMap),
                "Select at least one replay per map."
            );
        }

        this.perMap = perMap;
    }

    public int PerMap => perMap;

    public bool TryTake(string map, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A replay path is required.", nameof(path));
        }

        string key = string.IsNullOrWhiteSpace(map) ? "(unknown)" : map.Trim();
        List<string> paths;
        if (!selected.TryGetValue(key, out paths))
        {
            paths = new List<string>();
            selected.Add(key, paths);
        }

        int seen;
        identified.TryGetValue(key, out seen);
        identified[key] = seen + 1;

        if (paths.Count >= perMap)
        {
            return false;
        }

        paths.Add(path);
        return true;
    }

    public IReadOnlyList<MapReplaySample> Selected()
    {
        var samples = new List<MapReplaySample>();
        foreach (
            KeyValuePair<string, List<string>> pair in selected.OrderBy(
                pair => pair.Key,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            int filesIdentified;
            identified.TryGetValue(pair.Key, out filesIdentified);
            samples.Add(new MapReplaySample(pair.Key, pair.Value.ToList(), filesIdentified));
        }

        return samples;
    }
}
