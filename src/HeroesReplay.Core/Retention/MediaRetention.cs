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
    /// <summary>The media pruner's name in the housekeeping pass and <c>storage report</c>.</summary>
    public const string PrunerName = "media";

    public static void SweepAndLog(AppSettings settings, ILogger logger)
    {
        if (settings?.Housekeeping?.DryRun == true)
        {
            // Housekeeping:DryRun covers the in-role sweeps too: log, remove nothing (#487).
            HousekeepingPass.LogDryRun(Plan(settings, DateTimeOffset.UtcNow), logger);
            return;
        }

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

    public static RetentionSweep Sweep(AppSettings settings, DateTimeOffset utcNow) =>
        Plan(settings, utcNow).DeleteAll();

    /// <summary>The media pruner for the housekeeping pass (#487).</summary>
    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "Played replays after Retention:ReplayKeepDays; uploaded recordings; context folders after Retention:VideoKeepDays; recordings that can never be uploaded.",
            now => Plan(settings, now)
        );

    /// <summary>What a sweep would remove now, in the order it removes it. Writes nothing (#487).</summary>
    public static PrunePlan Plan(AppSettings settings, DateTimeOffset utcNow)
    {
        var plan = new PrunePlan(PrunerName);
        if (settings?.Retention?.Enabled == false || settings?.Location == null)
        {
            return plan;
        }

        int videoKeepDays =
            settings.Retention?.VideoKeepDays > 0 ? settings.Retention.VideoKeepDays : 3;
        int videoMaxDays =
            settings.Retention?.VideoMaxAgeDays > videoKeepDays
                ? settings.Retention.VideoMaxAgeDays
                : videoKeepDays + 4;
        int replayKeepDays =
            settings.Retention?.ReplayKeepDays > 0 ? settings.Retention.ReplayKeepDays : 30;
        string separator = string.IsNullOrEmpty(settings.StormReplay?.Seperator)
            ? "_"
            : settings.StormReplay.Seperator;

        string protectedId = ProtectNewestContext(settings.ContextsDirectory);
        HashSet<int> played = PlayedReplayIds.Read(settings.Location.DataDirectory);
        if (settings.HeroesProfileApi != null)
        {
            var replays = new ReplayRule(played, protectedId, separator, replayKeepDays, utcNow);
            PlanPlayedReplays(settings.StandardReplayCachePath, replays, plan);
            PlanPlayedReplays(settings.RequestedReplayCachePath, replays, plan);
        }

        TimeSpan grace =
            settings.Retention?.UnpublishedGrace > TimeSpan.Zero
                ? settings.Retention.UnpublishedGrace
                : TimeSpan.FromHours(1);
        PlanOldContexts(
            settings.ContextsDirectory,
            protectedId,
            new ContextRule(
                videoKeepDays,
                videoMaxDays,
                grace,
                utcNow,
                settings.YouTube?.DryRun == true
            ),
            plan
        );
        return plan;
    }

    private sealed record ReplayRule(
        HashSet<int> Played,
        string ProtectedId,
        string Separator,
        int KeepDays,
        DateTimeOffset UtcNow
    )
    {
        public DateTime Before => UtcNow.AddDays(-KeepDays).UtcDateTime;
    }

    /// <summary>
    /// <see cref="DryRun"/> is <c>YouTube:DryRun</c>: a recording the dry run planned is never
    /// sent, even while its entry waits, so VideoMaxAgeDays bounds it. With DryRun off a live
    /// uploader sends a recording an earlier dry run planned, so it waits like any other.
    /// </summary>
    private sealed record ContextRule(
        int KeepDays,
        int MaxDays,
        TimeSpan Grace,
        DateTimeOffset UtcNow,
        bool DryRun
    )
    {
        public DateTime KeepBefore => UtcNow.AddDays(-KeepDays).UtcDateTime;
        public DateTime DropBefore => UtcNow.AddDays(-MaxDays).UtcDateTime;
        public DateTime QuietBefore => (UtcNow - Grace).UtcDateTime;
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

    private static void PlanPlayedReplays(string directory, ReplayRule rule, PrunePlan plan)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (FileInfo replay in new DirectoryInfo(directory).EnumerateFiles("*.StormReplay"))
        {
            if (!TryReplayId(replay.Name, rule.Separator, out int id))
            {
                continue;
            }

            if (string.Equals(id.ToString(), rule.ProtectedId, StringComparison.Ordinal))
            {
                continue;
            }

            if (rule.Played.Contains(id) && replay.LastWriteTimeUtc < rule.Before)
            {
                plan.Add(
                    PruneItem.ForFile(
                        replay,
                        $"played replay older than Retention:ReplayKeepDays ({rule.KeepDays} days)"
                    )
                );
            }
        }
    }

    private static void PlanOldContexts(
        string contextsDirectory,
        string protectedId,
        ContextRule rule,
        PrunePlan plan
    )
    {
        if (string.IsNullOrWhiteSpace(contextsDirectory) || !Directory.Exists(contextsDirectory))
        {
            return;
        }

        var root = new DirectoryInfo(contextsDirectory);
        // OBS records into the Contexts root when it never got the replay's folder (#489). No
        // entry names such a file, so nothing can upload it: it goes once OBS is done with it.
        foreach (FileInfo stray in root.GetFiles("*.mp4"))
        {
            if (stray.LastWriteTimeUtc < rule.QuietBefore)
            {
                plan.Add(
                    PruneItem.ForFile(
                        stray,
                        "recording in the Contexts root, quiet for Retention:UnpublishedGrace: no replay folder or entry names it",
                        StrayWarning(stray.FullName)
                    )
                );
            }
        }

        foreach (DirectoryInfo dir in root.GetDirectories())
        {
            // A file this plan already removes is not looked at again for the same folder.
            var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool inserted = PendingUploadSize.IsInserted(dir.FullName, "youtube-entry.json");
            if (
                !string.Equals(dir.Name, protectedId, StringComparison.OrdinalIgnoreCase)
                && inserted
            )
            {
                // The video is on YouTube. Its entry may wait for publishAt, but the mp4 is not needed.
                foreach (FileInfo video in dir.GetFiles("*.mp4"))
                {
                    plan.Add(
                        PruneItem.ForFile(video, "uploaded recording: the video is on YouTube")
                    );
                    planned.Add(video.FullName);
                }
            }

            if (string.Equals(dir.Name, protectedId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool dryRun = dir.GetFiles(DryRunRecordings.PlanFileName).Length > 0;
            FileInfo[] videos = dir.GetFiles("*.mp4")
                .Where(video => !planned.Contains(video.FullName))
                .ToArray();
            DateTime written = dir.LastWriteTimeUtc;
            FileInfo entry = WaitingEntry(dir, inserted);
            if (entry != null && !(dryRun && rule.DryRun))
            {
                // A recording that waits for its insert is kept (#250). Without a recording the
                // entry can never upload: a recording retired before it was marked expired, or one
                // that never finished (#489). It goes once untouched for VideoKeepDays.
                DateTime touched =
                    entry.LastWriteTimeUtc > written ? entry.LastWriteTimeUtc : written;
                if (videos.Length == 0 && touched < rule.KeepBefore)
                {
                    plan.Add(
                        PruneItem.ForDirectory(
                            dir,
                            $"youtube-entry.json with no video id and no recording, untouched for Retention:VideoKeepDays ({rule.KeepDays} days): nothing in it can be uploaded"
                        )
                    );
                }

                continue;
            }

            bool remoteUpload =
                !dryRun && (inserted || dir.GetFiles("youtube-entry-uploaded.json").Length > 0);
            if (dryRun)
            {
                // YouTube:DryRun never sends it: the media goes after VideoMaxAgeDays, then the folder.
                bool retainedDryRun = PlanHeavyFilesOlderThan(
                    dir,
                    rule.DropBefore,
                    $"dry-run media older than Retention:VideoMaxAgeDays ({rule.MaxDays} days)",
                    planned,
                    plan,
                    warnNeverUploaded: true
                );
                if (!retainedDryRun && written < rule.DropBefore)
                {
                    plan.Add(
                        PruneItem.ForDirectory(
                            dir,
                            $"dry-run context folder older than Retention:VideoMaxAgeDays ({rule.MaxDays} days)"
                        )
                    );
                }

                continue;
            }

            if (dir.GetFiles(RetiredRecordings.EntryFileName).Length > 0)
            {
                // Retired before upload (#489): the uploader or the pending-bytes gate already
                // decided it goes, so a recording left behind (a delete that failed) goes too.
                foreach (
                    FileInfo video in videos.Where(video =>
                        video.LastWriteTimeUtc < rule.QuietBefore
                    )
                )
                {
                    plan.Add(
                        PruneItem.ForFile(
                            video,
                            "recording retired before upload (youtube-entry-expired.json)"
                        )
                    );
                    planned.Add(video.FullName);
                }
            }
            // The spectator writes the YouTube entry seconds after OBS stops. A recording with no
            // entry that has not been written for UnpublishedGrace can never be published, so it
            // goes now, not after VideoMaxAgeDays (#204). The newest context by time is not
            // always the live session, so the file's own write time protects it (#212).
            else if (videos.Length > 0 && !remoteUpload)
            {
                foreach (FileInfo video in videos)
                {
                    if (video.LastWriteTimeUtc >= rule.QuietBefore)
                    {
                        continue;
                    }

                    plan.Add(
                        PruneItem.ForFile(
                            video,
                            "recording with no youtube-entry.json, quiet for Retention:UnpublishedGrace",
                            NoEntryWarning(video.FullName)
                        )
                    );
                }

                continue;
            }

            bool retainedYoungMedia = PlanHeavyFilesOlderThan(
                dir,
                rule.KeepBefore,
                $"media older than Retention:VideoKeepDays ({rule.KeepDays} days)",
                planned,
                plan,
                warnNeverUploaded: false
            );
            if (!retainedYoungMedia && written < rule.KeepBefore)
            {
                plan.Add(
                    PruneItem.ForDirectory(
                        dir,
                        $"context folder older than Retention:VideoKeepDays ({rule.KeepDays} days)"
                    )
                );
            }
        }
    }

    /// <summary>
    /// The reason a recording without an entry goes. The sweep never deletes a recording that
    /// has an entry and waits for its insert: that one is eligible, and the spectator's
    /// pending-bytes guard (it stops ordinary recordings) keeps the disk in check (#250). When
    /// that guard trips, <see cref="StaleOrdinaryRecordings"/> clears the ones the uploader can
    /// never publish (#279).
    /// </summary>
    public static string NoEntryWarning(string path) =>
        "Removed recording that was never uploaded: "
        + path
        + " (reason: no-entry. It has no youtube-entry.json, so the uploader can never send it: the session ended before the publication decision, for example a crash or a stop mid-match, or the publication was withheld).";

    /// <summary>A recording OBS wrote into the Contexts root goes, and the operator hears why.</summary>
    public static string StrayWarning(string path) =>
        "Removed recording that was never uploaded: "
        + path
        + " (reason: contexts-root. OBS wrote it into the Contexts folder itself, not a replay's folder, so no entry names it and the uploader can never send it).";

    /// <summary>
    /// The entry of a recording that still waits for its insert: <c>youtube-entry.json</c> with
    /// no video id. An entry that has one only waits for publishAt, so its folder ages out like an
    /// upload. Null when there is none.
    /// </summary>
    private static FileInfo WaitingEntry(DirectoryInfo dir, bool inserted)
    {
        if (inserted)
        {
            return null;
        }

        var entry = new FileInfo(Path.Combine(dir.FullName, "youtube-entry.json"));
        return entry.Exists ? entry : null;
    }

    private static bool PlanHeavyFilesOlderThan(
        DirectoryInfo dir,
        DateTime deleteBefore,
        string reason,
        HashSet<string> planned,
        PrunePlan plan,
        bool warnNeverUploaded
    )
    {
        bool retainedYoungMedia = false;
        foreach (FileInfo heavy in dir.GetFiles("*.mp4").Concat(dir.GetFiles("*.StormReplay")))
        {
            if (planned.Contains(heavy.FullName))
            {
                continue;
            }

            if (heavy.LastWriteTimeUtc >= deleteBefore)
            {
                retainedYoungMedia = true;
                continue;
            }

            string warning =
                warnNeverUploaded
                && heavy.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? "Removed recording that was never uploaded: "
                        + heavy.FullName
                        + " (reason: dry-run. A dry run never sends it.)"
                    : null;
            plan.Add(PruneItem.ForFile(heavy, reason, warning));
            planned.Add(heavy.FullName);
        }

        return retainedYoungMedia;
    }

    internal static void DeleteDirectory(DirectoryInfo dir, RetentionSweep result)
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

    internal static void DeleteFile(string path, RetentionSweep result, string warning)
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
