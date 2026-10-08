using System;
using System.Globalization;
using System.IO;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// Recordings a dry run planned (#317). With <c>YouTube:DryRun</c> on, the uploader writes
/// <see cref="PlanFileName"/> beside the recording and never sends it, and the entry stays
/// <c>youtube-entry.json</c>, so <see cref="MediaRetention"/> keeps the context as one that waits
/// for its insert. Once the plan is written and an mp4 is older than
/// <c>Retention:DryRunRecordingMaxAge</c>, the mp4 goes. Only an mp4 at the top of the context
/// that the plan was written after is removed: the clips folder, <c>end.png</c>, the
/// <c>.StormReplay</c>, and the json files stay. With DryRun off, or a zero age, nothing is removed.
/// </summary>
public static class DryRunRecordings
{
    /// <summary>The plan the uploader writes in a dry run instead of calling YouTube.</summary>
    public const string PlanFileName = "youtube-dry-run.json";

    public static void SweepAndLog(AppSettings settings, ILogger logger)
    {
        RetentionSweep sweep = Sweep(settings, DateTimeOffset.UtcNow);
        if (sweep.DeletedFiles > 0)
        {
            logger?.LogInformation(
                "Removed {Files} dry-run recording(s) ({Megabytes} MB) older than Retention:DryRunRecordingMaxAge {MaxAge}. YouTube:DryRun never sends them; their dry-run plans, clips, and end.png stay.",
                sweep.DeletedFiles,
                sweep.FreedBytes / (1024 * 1024),
                settings.Retention.DryRunRecordingMaxAge.ToString("c", CultureInfo.InvariantCulture)
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
        TimeSpan maxAge = settings?.Retention?.DryRunRecordingMaxAge ?? TimeSpan.Zero;
        if (
            settings?.Retention?.Enabled == false
            || settings?.YouTube?.DryRun != true
            || maxAge <= TimeSpan.Zero
            || string.IsNullOrWhiteSpace(settings.Location?.DataDirectory)
        )
        {
            return result;
        }

        string contexts = settings.ContextsDirectory;
        DateTime oldBefore = (utcNow - maxAge).UtcDateTime;
        string[] directories;
        try
        {
            if (!Directory.Exists(contexts))
            {
                return result;
            }

            directories = Directory.GetDirectories(contexts);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result.Warnings.Add("Could not list " + contexts + ": " + e.Message);
            return result;
        }

        foreach (string directory in directories)
        {
            try
            {
                DeletePlannedRecordings(new DirectoryInfo(directory), oldBefore, result);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                result.Warnings.Add(
                    "Could not sweep dry-run recordings in " + directory + ": " + e.Message
                );
            }
        }

        return result;
    }

    private static void DeletePlannedRecordings(
        DirectoryInfo context,
        DateTime oldBefore,
        RetentionSweep result
    )
    {
        var plan = new FileInfo(Path.Combine(context.FullName, PlanFileName));
        if (!plan.Exists)
        {
            return;
        }

        // The plan is written after the recording is finished. A newer mp4 in the same context
        // (the replay spectated again) has no plan of its own yet, so it stays.
        DateTime planned = plan.LastWriteTimeUtc;
        foreach (FileInfo video in context.GetFiles("*.mp4", SearchOption.TopDirectoryOnly))
        {
            if (
                !video.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || video.LastWriteTimeUtc > planned
                || video.LastWriteTimeUtc >= oldBefore
            )
            {
                continue;
            }

            MediaRetention.DeleteFile(video.FullName, result, warning: null);
        }
    }
}
