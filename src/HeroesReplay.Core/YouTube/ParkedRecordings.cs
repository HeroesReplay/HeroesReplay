using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.YouTube.Outbox;

namespace HeroesReplay.Core.YouTube;

/// <summary>Why the uploader parked a recording, and since when (<see cref="ParkedRecordings.FileName"/>).</summary>
public sealed class ParkedRecording
{
    public string Reason { get; set; }
    public DateTimeOffset Since { get; set; }
    public string AttemptId { get; set; }
    public int? ReplayId { get; set; }
    public bool Requested { get; set; }
}

/// <summary>A parked recording as <c>check storage</c> shows it.</summary>
public sealed record ParkedRecordingView(
    int? ReplayId,
    string Reason,
    DateTimeOffset Since,
    long Bytes,
    bool Requested,
    string Requester,
    string Reward
);

/// <summary>
/// Recordings the uploader parked (#490): an interrupted upload whose automatic retries are used
/// (<see cref="OperatorRetry"/>), whose upload session YouTube no longer knows
/// (<see cref="SessionGone"/>), or whose replay is already on the channel (<see cref="OnChannel"/>).
/// Each kept its mp4 (2–4 GB), entry and folder forever, unseen, and counted towards the
/// pending-bytes gate. The uploader now writes <see cref="FileName"/> beside a parked recording
/// and removes it once the upload goes on. A duplicate already on the channel goes at once; the
/// others after <c>Retention:ParkedKeepDays</c>, each with its entry marked expired. A viewer's
/// request is never removed: past that age it waits for an operator, and <c>check storage</c>
/// names it with its requester and reward. Parked recordings no longer count as pending.
/// </summary>
public static class ParkedRecordings
{
    public const string FileName = "youtube-parked.json";
    public const string PrunerName = "parked-recordings";
    public const string OperatorRetry = "operator-retry";
    public const string SessionGone = "session-gone";
    public const string OnChannel = "on-channel";

    public static bool IsParked(string contextDirectory) =>
        !string.IsNullOrWhiteSpace(contextDirectory)
        && File.Exists(Path.Combine(contextDirectory, FileName));

    /// <summary>
    /// Records that the recording at <paramref name="recording"/> is parked for
    /// <paramref name="reason"/>. The first time it was parked for that reason stays.
    /// </summary>
    public static void Mark(
        string recording,
        string reason,
        string attemptId,
        YouTubeEntry entry,
        DateTimeOffset now
    )
    {
        string directory = Path.GetDirectoryName(recording);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        ParkedRecording existing = Read(directory);
        if (existing != null && string.Equals(existing.Reason, reason, StringComparison.Ordinal))
        {
            return;
        }

        var parked = new ParkedRecording
        {
            Reason = reason,
            Since = now,
            AttemptId = attemptId,
            ReplayId = entry?.ReplayId,
            Requested = entry?.Requested == true,
        };
        string path = Path.Combine(directory, FileName);
        string temp = path + ".tmp";
        File.WriteAllText(
            temp,
            JsonSerializer.Serialize(parked, YouTubeJsonContext.Indented.ParkedRecording)
        );
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Removes the mark: the upload goes on, or the recording is gone.</summary>
    public static void Clear(string recording)
    {
        string directory = Path.GetDirectoryName(recording ?? string.Empty);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string path = Path.Combine(directory, FileName);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static ParkedRecording Read(string contextDirectory)
    {
        string path = Path.Combine(contextDirectory ?? string.Empty, FileName);
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(
                    File.ReadAllText(path),
                    YouTubeJsonContext.Default.ParkedRecording
                )
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static IPruner Pruner(AppSettings settings) =>
        new Pruner(
            PrunerName,
            "Parked recordings (youtube-parked.json): a duplicate already on the channel at once, others after Retention:ParkedKeepDays; each entry is marked expired. A viewer's request is never removed.",
            now => Plan(settings, now),
            plan => Apply(settings, plan)
        );

    /// <summary>What the pruner removes now. Writes nothing.</summary>
    public static PrunePlan Plan(AppSettings settings, DateTimeOffset utcNow)
    {
        var plan = new PrunePlan(PrunerName);
        string entryName = settings?.YouTube?.EntryFileName;
        if (settings?.Retention?.Enabled == false || string.IsNullOrWhiteSpace(entryName))
        {
            return plan;
        }

        int keepDays = (settings.Retention ?? new RetentionSettings()).EffectiveParkedKeepDays;
        string attempts = PendingYouTubeUpload.AttemptsDirectory(settings.Location?.DataDirectory);
        foreach (
            (DirectoryInfo context, ParkedRecording parked, YouTubeEntry entry) in Waiting(settings)
        )
        {
            if (parked.Requested || entry.Requested || Sending(attempts, parked, entry))
            {
                continue;
            }

            bool duplicate = string.Equals(parked.Reason, OnChannel, StringComparison.Ordinal);
            if (!duplicate && utcNow - parked.Since < TimeSpan.FromDays(keepDays))
            {
                continue;
            }

            foreach (FileInfo video in context.GetFiles("*.mp4"))
            {
                string reason = duplicate
                    ? "parked: replay already on the channel, so this recording is a duplicate"
                    : $"parked ({parked.Reason}) since {parked.Since.ToString("u", CultureInfo.InvariantCulture)}, longer than Retention:ParkedKeepDays ({keepDays})";
                plan.Add(
                    PruneItem.ForFile(
                        video,
                        reason,
                        "Removed parked recording that was never uploaded: "
                            + video.FullName
                            + " (reason: parked-"
                            + parked.Reason
                            + ". "
                            + reason
                            + "; its attempt stays for an operator, and its entry is marked expired.)"
                    )
                );
            }
        }

        return plan;
    }

    private static RetentionSweep Apply(AppSettings settings, PrunePlan plan)
    {
        RetentionSweep sweep = plan.DeleteAll();
        foreach (PruneItem item in plan.Items.Where(item => !File.Exists(item.Path)))
        {
            RetiredRecordings.MarkExpired(item.Path, settings?.YouTube?.EntryFileName);
            Clear(item.Path);
        }

        return sweep;
    }

    /// <summary>Every parked recording, with its requester and reward when it is a request.</summary>
    public static IReadOnlyList<ParkedRecordingView> Summarize(AppSettings settings)
    {
        var views = new List<ParkedRecordingView>();
        if (string.IsNullOrWhiteSpace(settings?.YouTube?.EntryFileName))
        {
            return views;
        }

        string requests =
            settings.HeroesProfileApi == null ? null : settings.RequestedReplayCachePath;
        foreach (
            (DirectoryInfo context, ParkedRecording parked, YouTubeEntry entry) in Waiting(settings)
        )
        {
            bool requested = parked.Requested || entry.Requested;
            RewardRequest request =
                requested && entry.ReplayId is int id
                    ? CachedRequestReward.FindById(requests, id)?.Request
                    : null;
            views.Add(
                new ParkedRecordingView(
                    entry.ReplayId ?? parked.ReplayId,
                    parked.Reason,
                    parked.Since,
                    context.GetFiles("*.mp4").Sum(video => video.Length),
                    requested,
                    request?.Login,
                    request?.RewardTitle
                )
            );
        }

        return views;
    }

    /// <summary>Contexts with a mark, a recording, and an entry that still waits for its insert.</summary>
    private static IEnumerable<(
        DirectoryInfo Context,
        ParkedRecording Parked,
        YouTubeEntry Entry
    )> Waiting(AppSettings settings)
    {
        string contexts =
            settings?.Location?.DataDirectory == null ? null : settings.ContextsDirectory;
        if (string.IsNullOrWhiteSpace(contexts) || !Directory.Exists(contexts))
        {
            yield break;
        }

        foreach (DirectoryInfo context in new DirectoryInfo(contexts).GetDirectories())
        {
            ParkedRecording parked = Read(context.FullName);
            if (parked == null || context.GetFiles("*.mp4").Length == 0)
            {
                continue;
            }

            YouTubeEntry entry = PendingYouTubeUpload.ReadEntry(
                Path.Combine(context.FullName, settings.YouTube.EntryFileName)
            );
            if (entry == null || !string.IsNullOrWhiteSpace(entry.VideoId))
            {
                continue;
            }

            yield return (context, parked, entry);
        }
    }

    /// <summary>A send under way is never touched, whatever the mark says.</summary>
    private static bool Sending(string attempts, ParkedRecording parked, YouTubeEntry entry)
    {
        string attemptId =
            parked.AttemptId
            ?? (
                entry.ReplayId is int id
                    ? "replay-" + id.ToString(CultureInfo.InvariantCulture)
                    : null
            );
        if (string.IsNullOrWhiteSpace(attempts) || string.IsNullOrWhiteSpace(attemptId))
        {
            return false;
        }

        string path = Path.Combine(attempts, attemptId, UploadAttemptStore.ManifestFileName);
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            UploadAttemptResult read = UploadAttemptManifestCodec.Read(
                File.ReadAllText(path),
                attemptId
            );
            return read.Succeeded
                && read.Manifest.State
                    is UploadAttemptState.Uploading
                        or UploadAttemptState.UploadPending;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
