using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Analysis;
using HeroesReplay.Core.Services.Queue;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Retention;

public sealed class RetentionSweep
{
    public int DeletedFiles { get; set; }
    public long FreedBytes { get; set; }
    public List<string> Warnings { get; } = new();
}

public static class MediaRetention
{
    public static void SweepAndLog(AppSettings settings, ILogger logger)
    {
        RetentionSweep sweep = Sweep(settings, DateTimeOffset.UtcNow);
        if (sweep.DeletedFiles > 0)
        {
            logger?.LogInformation(
                "Removed {Files} old replay files ({Megabytes} MB).",
                sweep.DeletedFiles,
                sweep.FreedBytes / (1024 * 1024)
            );
        }

        foreach (string warning in sweep.Warnings)
        {
            logger?.LogWarning("{RetentionWarning}", warning);
        }
    }

    public static RetentionSweep Sweep(AppSettings settings, DateTimeOffset utcNow)
    {
        var result = new RetentionSweep();
        if (settings?.Retention?.Enabled == false || settings?.Location == null)
        {
            return result;
        }

        int videoKeepDays =
            settings.Retention?.VideoKeepDays > 0 ? settings.Retention.VideoKeepDays : 3;
        int videoMaxDays =
            settings.Retention?.VideoMaxAgeDays > videoKeepDays
                ? settings.Retention.VideoMaxAgeDays
                : videoKeepDays + 4;
        int replayKeepDays =
            settings.Retention?.ReplayKeepDays > 0 ? settings.Retention.ReplayKeepDays : 30;
        DateTimeOffset keepBefore = utcNow.AddDays(-videoKeepDays);
        DateTimeOffset dropBefore = utcNow.AddDays(-videoMaxDays);
        DateTimeOffset replayBefore = utcNow.AddDays(-replayKeepDays);
        string separator = string.IsNullOrEmpty(settings.StormReplay?.Seperator)
            ? "_"
            : settings.StormReplay.Seperator;

        string protectedId = ProtectNewestContext(settings.ContextsDirectory);
        HashSet<int> played = PlayedReplayIds.Read(settings.Location.DataDirectory);
        if (settings.HeroesProfileApi != null)
        {
            DeletePlayedReplays(
                settings.StandardReplayCachePath,
                played,
                protectedId,
                separator,
                replayBefore,
                result
            );
            DeletePlayedReplays(
                settings.RequestedReplayCachePath,
                played,
                protectedId,
                separator,
                replayBefore,
                result
            );
        }
        DeleteOldContexts(settings.ContextsDirectory, protectedId, keepBefore, dropBefore, result);
        return result;
    }

    private static string ProtectNewestContext(string contextsDirectory)
    {
        if (string.IsNullOrWhiteSpace(contextsDirectory) || !Directory.Exists(contextsDirectory))
        {
            return null;
        }

        DirectoryInfo newest = new DirectoryInfo(contextsDirectory)
            .GetDirectories()
            .OrderByDescending(dir => dir.LastWriteTimeUtc)
            .FirstOrDefault();
        return newest?.Name;
    }

    private static void DeletePlayedReplays(
        string directory,
        HashSet<int> played,
        string protectedId,
        string separator,
        DateTimeOffset replayBefore,
        RetentionSweep result
    )
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (string path in Directory.EnumerateFiles(directory, "*.StormReplay"))
        {
            string name = Path.GetFileName(path);
            if (!TryReplayId(name, separator, out int id))
            {
                continue;
            }

            if (string.Equals(id.ToString(), protectedId, StringComparison.Ordinal))
            {
                continue;
            }

            if (played.Contains(id) && File.GetLastWriteTimeUtc(path) < replayBefore.UtcDateTime)
            {
                DeleteFile(path, result, warning: null);
            }
        }
    }

    private static void DeleteOldContexts(
        string contextsDirectory,
        string protectedId,
        DateTimeOffset keepBefore,
        DateTimeOffset dropBefore,
        RetentionSweep result
    )
    {
        if (string.IsNullOrWhiteSpace(contextsDirectory) || !Directory.Exists(contextsDirectory))
        {
            return;
        }

        foreach (DirectoryInfo dir in new DirectoryInfo(contextsDirectory).GetDirectories())
        {
            if (string.Equals(dir.Name, protectedId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ClipsStillNeedSource(dir))
            {
                result.Warnings.Add(
                    "Kept " + dir.FullName + " because a pentakill clip is not cut yet."
                );
                continue;
            }

            DateTime written = dir.LastWriteTimeUtc;
            FileInfo[] videos = dir.GetFiles("*.mp4");
            bool uploaded =
                dir.GetFiles("youtube-entry-uploaded.json").Length > 0
                || dir.GetFiles("youtube-dry-run.json").Length > 0;
            if (videos.Length > 0 && !uploaded)
            {
                if (written >= dropBefore.UtcDateTime)
                {
                    continue;
                }

                foreach (FileInfo video in videos)
                {
                    DeleteFile(
                        video.FullName,
                        result,
                        "Removed recording that was never uploaded: " + video.FullName
                    );
                }

                continue;
            }

            foreach (FileInfo heavy in dir.GetFiles("*.mp4").Concat(dir.GetFiles("*.StormReplay")))
            {
                DeleteFile(heavy.FullName, result, warning: null);
            }

            if (written < keepBefore.UtcDateTime)
            {
                try
                {
                    long bytes = dir.Exists
                        ? dir.EnumerateFiles("*", SearchOption.AllDirectories)
                            .Sum(file => file.Length)
                        : 0;
                    dir.Delete(recursive: true);
                    result.DeletedFiles++;
                    result.FreedBytes += bytes;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    result.Warnings.Add("Could not remove " + dir.FullName + ": " + e.Message);
                }
            }
        }
    }

    private static bool ClipsStillNeedSource(DirectoryInfo dir)
    {
        string path = Path.Combine(dir.FullName, MatchClipList.FileName);
        if (!File.Exists(path))
        {
            return false;
        }

        IReadOnlyList<MatchClipEntry> entries;
        try
        {
            entries = MatchClipList.Read(path);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return true;
        }

        foreach (MatchClipEntry entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry?.File))
            {
                continue;
            }

            if (!File.Exists(Path.Combine(dir.FullName, entry.File)))
            {
                return true;
            }
        }

        return false;
    }

    private static void DeleteFile(string path, RetentionSweep result, string warning)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return;
            }

            long bytes = info.Length;
            info.Delete();
            result.DeletedFiles++;
            result.FreedBytes += bytes;
            if (!string.IsNullOrWhiteSpace(warning))
            {
                result.Warnings.Add(warning);
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            result.Warnings.Add("Could not remove " + path + ": " + e.Message);
        }
    }

    private static bool TryReplayId(string fileName, string separator, out int replayId)
    {
        replayId = 0;
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrEmpty(separator))
        {
            return false;
        }

        string head = fileName.Split(separator)[0];
        return int.TryParse(head, out replayId) && replayId > 0;
    }
}
