using System;
using System.IO;
using System.Text.Json;
using HeroesReplay.Core.YouTube;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// Bytes of recordings still waiting for a videos.insert. A recording whose entry already
/// has a VideoId was inserted and only waits for its publishAt, so it is not counted.
/// </summary>
public static class PendingUploadSize
{
    public static long Bytes(
        string contextsDirectory,
        string entryFileName,
        string uploadedFileName
    ) => Measure(contextsDirectory, entryFileName, uploadedFileName).Bytes;

    /// <summary>Recordings still waiting for a videos.insert.</summary>
    public static int Count(
        string contextsDirectory,
        string entryFileName,
        string uploadedFileName
    ) => Measure(contextsDirectory, entryFileName, uploadedFileName).Count;

    private static (long Bytes, int Count) Measure(
        string contextsDirectory,
        string entryFileName,
        string uploadedFileName
    )
    {
        long total = 0;
        int count = 0;
        foreach (
            string path in PendingYouTubeUpload.Find(
                contextsDirectory,
                entryFileName,
                uploadedFileName
            )
        )
        {
            try
            {
                var info = new FileInfo(path);
                if (
                    info.Exists
                    && info.Length > 0
                    && !IsInserted(info.DirectoryName, entryFileName)
                )
                {
                    total += info.Length;
                    count++;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return (total, count);
    }

    public static bool IsInserted(string directory, string entryFileName)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(entryFileName))
        {
            return false;
        }

        string entryPath = Path.Combine(directory, entryFileName);
        if (!File.Exists(entryPath))
        {
            return false;
        }

        try
        {
            YouTubeEntry entry = JsonSerializer.Deserialize<YouTubeEntry>(
                File.ReadAllText(entryPath)
            );
            return !string.IsNullOrWhiteSpace(entry?.VideoId);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
