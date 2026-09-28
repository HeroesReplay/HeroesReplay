using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HeroesReplay.Core.Services.YouTube;

public static class PendingYouTubeUpload
{
    public static IReadOnlyList<string> Find(
        string contextsDirectory,
        string entryFileName,
        string uploadedFileName
    )
    {
        var found = new List<string>();
        if (
            string.IsNullOrWhiteSpace(contextsDirectory)
            || string.IsNullOrWhiteSpace(entryFileName)
            || !Directory.Exists(contextsDirectory)
        )
        {
            return found;
        }

        foreach (string directory in Directory.GetDirectories(contextsDirectory))
        {
            if (!File.Exists(Path.Combine(directory, entryFileName)))
            {
                continue;
            }

            if (
                !string.IsNullOrWhiteSpace(uploadedFileName)
                && File.Exists(Path.Combine(directory, uploadedFileName))
            )
            {
                continue;
            }

            string newest = Directory
                .GetFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(newest))
            {
                found.Add(newest);
            }
        }

        found.Sort(
            (left, right) =>
                File.GetLastWriteTimeUtc(left).CompareTo(File.GetLastWriteTimeUtc(right))
        );
        return found;
    }
}
