using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Requests;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Retention;

public sealed class RetentionSweep
{
    public int DeletedFiles { get; set; }
    public long FreedBytes { get; set; }
    public List<string> Warnings { get; } = new();

    /// <summary>
    /// Files another process still had open (OBS finishing a recording, the uploader's handle).
    /// The same rule picks them again on the next sweep, so they are not warnings (#207).
    /// </summary>
    public List<string> InUse { get; } = new();
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

        foreach (string busy in sweep.InUse)
        {
            logger?.LogInformation(
                "{Path} is still open in another process. The next sweep removes it.",
                busy
            );
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
        TimeSpan grace =
            settings.Retention?.UnpublishedGrace > TimeSpan.Zero
                ? settings.Retention.UnpublishedGrace
                : TimeSpan.FromHours(1);
        DeleteOldContexts(
            settings.ContextsDirectory,
            protectedId,
            keepBefore,
            dropBefore,
            utcNow - grace,
            result
        );
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
        DateTimeOffset quietBefore,
        RetentionSweep result
    )
    {
        if (string.IsNullOrWhiteSpace(contextsDirectory) || !Directory.Exists(contextsDirectory))
        {
            return;
        }

        foreach (DirectoryInfo dir in new DirectoryInfo(contextsDirectory).GetDirectories())
        {
            bool inserted = PendingUploadSize.IsInserted(dir.FullName, "youtube-entry.json");
            if (
                !string.Equals(dir.Name, protectedId, StringComparison.OrdinalIgnoreCase)
                && inserted
            )
            {
                // The video is on YouTube. Its entry may wait for publishAt, but the mp4 is not needed.
                foreach (FileInfo video in dir.GetFiles("*.mp4"))
                {
                    DeleteFile(video.FullName, result, warning: null);
                }
            }

            if (IsProtectedContext(dir, protectedId, inserted))
            {
                continue;
            }

            bool dryRun = dir.GetFiles("youtube-dry-run.json").Length > 0;
            bool remoteUpload =
                !dryRun && (inserted || dir.GetFiles("youtube-entry-uploaded.json").Length > 0);
            FileInfo[] videos = dir.GetFiles("*.mp4");
            DateTime written = dir.LastWriteTimeUtc;
            if (dryRun)
            {
                DeleteHeavyFilesOlderThan(dir, dropBefore, result, warnNeverUploaded: true);
                continue;
            }

            // The spectator writes the YouTube entry seconds after OBS stops. A recording with no
            // entry that has not been written for UnpublishedGrace can never be published, so it
            // goes now, not after VideoMaxAgeDays (#204). The newest context by time is not
            // always the live session, so the file's own write time protects it (#212).
            if (videos.Length > 0 && !remoteUpload)
            {
                foreach (FileInfo video in videos)
                {
                    if (video.LastWriteTimeUtc >= quietBefore.UtcDateTime)
                    {
                        continue;
                    }

                    DeleteFile(
                        video.FullName,
                        result,
                        "Removed recording that was never uploaded: " + video.FullName
                    );
                }

                continue;
            }

            bool retainedYoungMedia = DeleteHeavyFilesOlderThan(
                dir,
                keepBefore,
                result,
                warnNeverUploaded: false
            );
            if (!retainedYoungMedia && written < keepBefore.UtcDateTime)
            {
                DeleteDirectory(dir, result);
            }
        }
    }

    /// <summary>
    /// The newest context and a recording still waiting for its insert are kept. An entry that
    /// already has a video id only waits for publishAt, so its context ages out like an upload.
    /// </summary>
    private static bool IsProtectedContext(DirectoryInfo dir, string protectedId, bool inserted)
    {
        if (string.Equals(dir.Name, protectedId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !inserted && dir.GetFiles("youtube-entry.json").Length > 0;
    }

    private static bool DeleteHeavyFilesOlderThan(
        DirectoryInfo dir,
        DateTimeOffset deleteBefore,
        RetentionSweep result,
        bool warnNeverUploaded
    )
    {
        bool retainedYoungMedia = false;
        foreach (FileInfo heavy in dir.GetFiles("*.mp4").Concat(dir.GetFiles("*.StormReplay")))
        {
            if (heavy.LastWriteTimeUtc >= deleteBefore.UtcDateTime)
            {
                retainedYoungMedia = true;
                continue;
            }

            string warning =
                warnNeverUploaded
                && heavy.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? "Removed recording that was never uploaded: " + heavy.FullName
                    : null;
            DeleteFile(heavy.FullName, result, warning);
        }

        return retainedYoungMedia;
    }

    private static void DeleteDirectory(DirectoryInfo dir, RetentionSweep result)
    {
        try
        {
            long bytes = dir.Exists
                ? dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length)
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
        catch (IOException e) when (IsInUse(e))
        {
            result.InUse.Add(path);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            result.Warnings.Add("Could not remove " + path + ": " + e.Message);
        }
    }

    /// <summary>ERROR_SHARING_VIOLATION (32) or ERROR_LOCK_VIOLATION (33).</summary>
    public static bool IsInUse(IOException e) => (e.HResult & 0xFFFF) is 32 or 33;

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
