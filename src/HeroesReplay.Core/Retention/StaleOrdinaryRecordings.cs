using System;
using System.Globalization;
using System.IO;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Outbox;
using HeroesReplay.Core.YouTube.Publication;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// Ordinary recordings that still wait for their <c>videos.insert</c> although the game is older
/// than <c>ReplayMedia:OrdinaryCandidateMaxAge</c>. On a live public listing the uploader refuses
/// such a recording as stale and deletes it the next time it plans it, so it can never be
/// published. When the pending-bytes gate trips, the spectator clears them first, so they do not
/// hold back a new recording (#279).
/// Only a recording the uploader itself would delete goes. A request, a notable or high-skill
/// replay, a recording that holds a publication slot or whose send started, one without a
/// readable entry or media decision, and the replay about to launch are kept.
/// </summary>
public static class StaleOrdinaryRecordings
{
    public static RetentionSweep Clear(
        AppSettings settings,
        DateTimeOffset utcNow,
        int? keepReplayId = null
    )
    {
        var result = new RetentionSweep();
        YouTubeSettings youtube = settings?.YouTube;
        string data = settings?.Location?.DataDirectory;
        if (
            settings?.Retention?.Enabled == false
            || youtube == null
            || string.IsNullOrWhiteSpace(data)
            || string.IsNullOrWhiteSpace(youtube.EntryFileName)
        )
        {
            return result;
        }

        // A dry run never deletes a recording, and a private listing still sends an old one.
        if (!youtube.Enabled || youtube.DryRun || !YouTubeListing.IsPublic(youtube))
        {
            return result;
        }

        ReplayMediaPolicySettings media = settings.ReplayMedia ?? new ReplayMediaPolicySettings();
        string attempts = PendingYouTubeUpload.AttemptsDirectory(data);
        string ledger = PublicationReservation.PathFor(data);
        foreach (
            string recording in PendingYouTubeUpload.Find(
                settings.ContextsDirectory,
                youtube.EntryFileName,
                youtube.EntryFileNameUploaded
            )
        )
        {
            YouTubeEntry entry = PendingYouTubeUpload.ReadEntry(
                Path.Combine(Path.GetDirectoryName(recording) ?? "", youtube.EntryFileName)
            );
            if (!IsStale(entry, attempts, ledger, media, utcNow, keepReplayId))
            {
                continue;
            }

            MediaRetention.DeleteFile(recording, result, StaleWarning(recording, entry, media));
        }

        return result;
    }

    private static bool IsStale(
        YouTubeEntry entry,
        string attempts,
        string ledger,
        ReplayMediaPolicySettings media,
        DateTimeOffset utcNow,
        int? keepReplayId
    )
    {
        if (
            entry == null
            || entry.Requested
            || !string.IsNullOrWhiteSpace(entry.VideoId)
            || entry.ReplayId is not int replayId
            || replayId <= 0
            || replayId == keepReplayId
        )
        {
            return false;
        }

        // The uploader's work key and the media decision's attempt id are both replay-<id>.
        string key = "replay-" + replayId.ToString(CultureInfo.InvariantCulture);
        if (PublicationReservation.HeldAt(ledger, key) != null)
        {
            return false;
        }

        UploadAttemptManifest manifest = ReadManifest(attempts, key);
        if (
            manifest == null
            || manifest.State
                is UploadAttemptState.Uploading
                    or UploadAttemptState.AmbiguousUpload
                    or UploadAttemptState.Uploaded
        )
        {
            return false;
        }

        PublicationSendFacts facts = PublicationSendFacts.FromDecision(
            MediaPolicyManifest.ToDecision(manifest),
            entry.RecordedAtUtc
        );
        return !facts.Incomplete
            && !facts.AlreadyPublished
            && !facts.Uncorrelated
            && PublicationSchedule.PastOrdinaryAge(media, facts, utcNow);
    }

    private static UploadAttemptManifest ReadManifest(string attempts, string attemptId)
    {
        if (string.IsNullOrWhiteSpace(attempts))
        {
            return null;
        }

        string path = Path.Combine(attempts, attemptId, UploadAttemptStore.ManifestFileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            UploadAttemptResult read = UploadAttemptManifestCodec.Read(
                File.ReadAllText(path),
                attemptId
            );
            return read.Succeeded ? read.Manifest : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string StaleWarning(
        string path,
        YouTubeEntry entry,
        ReplayMediaPolicySettings media
    ) =>
        "Removed recording that was eligible but never uploaded: "
        + path
        + " (reason: stale. Ordinary replay "
        + entry?.ReplayId?.ToString(CultureInfo.InvariantCulture)
        + " was played at "
        + entry?.RecordedAtUtc?.ToString("u", CultureInfo.InvariantCulture)
        + ", older than ReplayMedia:OrdinaryCandidateMaxAge "
        + media?.OrdinaryCandidateMaxAge.ToString("c", CultureInfo.InvariantCulture)
        + ", so the uploader can never publish it. The pending-bytes gate tripped, so it was cleared before the next recording.)";
}
