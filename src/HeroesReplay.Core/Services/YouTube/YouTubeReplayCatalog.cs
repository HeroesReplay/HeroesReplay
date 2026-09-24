using System;
using System.IO;

namespace HeroesReplay.Core.Services.YouTube;

public static class YouTubeReplayCatalog
{
    public const string FileName = "youtube-replay-ids.txt";
    private static readonly object Gate = new();

    public static string PathFor(string dataDirectory) =>
        string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.Combine(dataDirectory, FileName);

    public static bool Contains(string path, int replayId)
    {
        if (replayId <= 0 || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        lock (Gate)
        {
            return ContainsUnlocked(path, replayId);
        }
    }

    public static void Remember(string path, int replayId)
    {
        if (replayId <= 0 || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (Gate)
        {
            if (ContainsUnlocked(path, replayId))
            {
                return;
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(path, replayId + Environment.NewLine);
        }
    }

    private static bool ContainsUnlocked(string path, int replayId)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        foreach (string line in File.ReadLines(path))
        {
            if (int.TryParse(line.Trim(), out int id) && id == replayId)
            {
                return true;
            }
        }

        return false;
    }
}
