using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Publication;
using HeroesReplay.Core.YouTube.Quota;

namespace HeroesReplay.Core.MediaPolicy;

/// <summary>What the pipeline holds when the next replay launches.</summary>
public sealed class RecordingCapInput
{
    public ReplayMediaPriority Priority { get; init; }

    /// <summary>Recordings on disk that still wait for their <c>videos.insert</c>.</summary>
    public int PendingUploads { get; init; }

    /// <summary>Publication slots after now: uploads (or sends) that are not public yet.</summary>
    public int ScheduledAhead { get; init; }

    /// <summary>YouTube is enabled and not a dry run.</summary>
    public bool Live { get; init; }

    /// <summary><c>YouTube:PrivacyStatus</c> is public, so the pacing rules apply.</summary>
    public bool PublicListing { get; init; }

    /// <summary>Upload calls a day: the bucket less its reserve, and no more than MaxInsertsPerQuotaDay.</summary>
    public int UploadsPerDay { get; init; }
}

public readonly record struct RecordingCapDecision(
    bool Allow,
    string Reason,
    int InFlight,
    int Capacity
);

/// <summary>
/// Records only roughly what can be uploaded and published (#250). The media policy decides
/// what is worth recording. This cap decides whether the pipeline can still take one more: a
/// replay that is not a viewer request is recorded while the recordings waiting for upload plus
/// the uploads waiting for their publish time stay under what the pacing rules publish within
/// <see cref="ReplayMediaPolicySettings.MaxPublishAhead"/>, and the waiting recordings stay under
/// one day of upload calls. A viewer request is always recorded. A recording made past this
/// point would wait on disk until it is too old to publish, and then be deleted.
/// </summary>
public static class RecordingCap
{
    public const string Requested = "cap-requested";
    public const string Off = "cap-off";
    public const string NotLive = "cap-not-live";
    public const string Room = "cap-room";
    public const string PublicationFull = "cap-publication-full";
    public const string UploadBacklog = "cap-upload-backlog";

    public static RecordingCapDecision Decide(
        RecordingCapInput input,
        ReplayMediaPolicySettings settings
    )
    {
        settings ??= new ReplayMediaPolicySettings();
        input ??= new RecordingCapInput();
        int pending = Math.Max(0, input.PendingUploads);
        int ahead = Math.Max(0, input.ScheduledAhead);
        if (input.Priority == ReplayMediaPriority.Requested)
        {
            return new RecordingCapDecision(true, Requested, pending + ahead, -1);
        }

        if (!settings.CapRecordingToPublication)
        {
            return new RecordingCapDecision(true, Off, pending + ahead, -1);
        }

        // A dry run or a disabled uploader sends nothing, so nothing piles up for YouTube.
        if (!input.Live)
        {
            return new RecordingCapDecision(true, NotLive, pending + ahead, -1);
        }

        int uploads = Math.Max(0, input.UploadsPerDay);
        if (pending >= uploads)
        {
            return new RecordingCapDecision(false, UploadBacklog, pending, uploads);
        }

        if (!input.PublicListing)
        {
            return new RecordingCapDecision(true, Room, pending, uploads);
        }

        int capacity = PublicationCapacity(settings, uploads);
        int inFlight = pending + ahead;
        return inFlight >= capacity
            ? new RecordingCapDecision(false, PublicationFull, inFlight, capacity)
            : new RecordingCapDecision(true, Room, inFlight, capacity);
    }

    /// <summary>
    /// Videos that are not requests the pacing rules publish within MaxPublishAhead (at least
    /// one day), and no more than the upload calls of those days can send.
    /// </summary>
    public static int PublicationCapacity(ReplayMediaPolicySettings settings, int uploadsPerDay)
    {
        settings ??= new ReplayMediaPolicySettings();
        double days = Math.Max(1.0, settings.MaxPublishAhead.TotalDays);
        double perDay = Math.Min(
            PublicationSchedule.OrdinaryPublishPerDay(settings),
            Math.Max(0, uploadsPerDay)
        );
        return (int)Math.Floor(perDay * days);
    }

    /// <summary>Reads the pending recordings and the publication slots this decision needs.</summary>
    public static RecordingCapInput Measure(
        AppSettings settings,
        ReplayMediaPriority priority,
        DateTimeOffset now
    )
    {
        YouTubeSettings youtube = settings?.YouTube ?? new YouTubeSettings();
        ReplayMediaPolicySettings media = settings?.ReplayMedia ?? new ReplayMediaPolicySettings();
        string data = settings?.Location?.DataDirectory;
        return new RecordingCapInput
        {
            Priority = priority,
            PendingUploads = string.IsNullOrWhiteSpace(data)
                ? 0
                : PendingUploadSize.Count(
                    settings.ContextsDirectory,
                    youtube.EntryFileName,
                    youtube.EntryFileNameUploaded
                ),
            ScheduledAhead = PublicationReservation.CountAfter(
                PublicationReservation.PathFor(data),
                now
            ),
            Live = youtube.Enabled && !youtube.DryRun,
            PublicListing = YouTubeListing.IsPublic(youtube),
            UploadsPerDay = Math.Min(
                YouTubeQuotaUnits.UsableUploadCalls(youtube),
                Math.Max(0, media.MaxInsertsPerQuotaDay)
            ),
        };
    }
}
