using System.Collections.Generic;
using System.IO;

namespace HeroesReplay.Core.Services.Queue;

public static class PlayedReplayIds
{
    public const string FileName = "spectated-ids.txt";

    public static HashSet<int> Read(string dataDirectory)
    {
        var ids = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return ids;
        }

        string path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path))
        {
            return ids;
        }

        foreach (string line in File.ReadLines(path))
        {
            if (int.TryParse(line?.Trim(), out int id) && id > 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
