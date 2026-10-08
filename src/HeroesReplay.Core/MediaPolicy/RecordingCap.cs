using System;
using System.Globalization;
using System.IO;
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

    /// <summary>
    /// Recordings on disk that still wait for their <c>videos.insert</c> and need a publication
    /// slot. One that already holds a slot after now is in <see cref="ScheduledAhead"/> only.
    /// </summary>
    public int PendingUploads { get; init; }

    /// <summary>Publication slots after now: uploads (or sends) that are not public yet.</summary>
    public int ScheduledAhead { get; init; }

    /// <summary>YouTube is enabled and not a dry run.</summary>
    public bool Live { get; init; }

    /// <summary><c>YouTube:PrivacyStatus</c> is public, so the pacing rules apply.</summary>
    public bool PublicListing { get; init; }

    /// <summary>Upload calls a day: the bucket less its reserve, and no more than MaxInsertsPerQuotaDay.</summary>
    public int UploadsPerDay { get; init; }

    /// <summary>
    /// When this replay stops being a publication candidate: the decision's
    /// <see cref="ReplayMediaDecision.CandidateExpiresAtUtc"/>, its game time plus its class's
    /// maximum age. An ordinary replay past it is never sent. Null when the game time is unknown.
    /// </summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>When the decision is made.</summary>
    public DateTimeOffset Now { get; init; }
}

/// <param name="Allow">The replay is recorded.</param>
/// <param name="Reason">One of the <see cref="RecordingCap"/> reasons, <c>cap-...</c>.</param>
/// <param name="Pending">Recordings that wait for their upload and need a slot.</param>
/// <param name="Scheduled">Uploads (or sends) that hold a slot after now.</param>
/// <param name="Capacity">
/// The slots the publication window holds, or a day of upload calls for the upload backlog and a
/// private listing. -1 when nothing caps the replay.
/// </param>
/// <param name="Wait">
/// How long this replay's recording would wait on disk for its slot, when the window is full.
/// Null when it is not computed.
/// </param>
/// <param name="ExpiresAtUtc">When the replay stops being a publication candidate, if known.</param>
public readonly record struct RecordingCapDecision(
    bool Allow,
    string Reason,
    int Pending,
    int Scheduled,
    int Capacity,
    TimeSpan? Wait = null,
    DateTimeOffset? ExpiresAtUtc = null
)
{
    public int InFlight => Pending + Scheduled;

    /// <summary>One sentence for the log: what waits, and why this replay is recorded or not.</summary>
    public string Summary
    {
        get
        {
            string counts = string.Create(
                CultureInfo.InvariantCulture,
                $"{Pending} recording(s) wait for upload and {Scheduled} upload(s) wait for their publish time"
            );
            return Reason switch
            {
                RecordingCap.UploadBacklog => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{counts}; {Pending} waiting is a day of upload calls ({Capacity})."
                ),
                RecordingCap.Room when Capacity >= 0 => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{counts}; the publication window holds {Capacity}, so its slot is free now."
                ),
                RecordingCap.Queued => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{counts}; the publication window holds {Capacity}. Its upload slot opens in about {Hours(Wait)} h, at least {Hours(RecordingCap.SendSlack)} h before it expires at {ExpiresAtUtc:u}."
                ),
                RecordingCap.PublicationFull when ExpiresAtUtc == null => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{counts}; the publication window holds {Capacity}, and the replay's game time is unknown, so it cannot wait for a slot."
                ),
                RecordingCap.PublicationFull => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{counts}; the publication window holds {Capacity}. Its upload slot would open in about {Hours(Wait)} h, less than {Hours(RecordingCap.SendSlack)} h before it expires at {ExpiresAtUtc:u}, so it would not be sent."
                ),
                _ => counts + ".",
            };
        }
    }

    private static string Hours(TimeSpan? span) =>
        Math.Max(0, span?.TotalHours ?? 0).ToString("0", CultureInfo.InvariantCulture);
}

/// <summary>
/// Records only what can be uploaded and published (#250, #370). The media policy decides what is
/// worth recording. This cap decides whether the pipeline can still take one more: whether a
/// replay that is not a viewer request would be sent before it stops being a candidate. The
/// publication window (<see cref="ReplayMediaPolicySettings.MaxPublishAhead"/>) holds what the
/// pacing rules publish in it. While it has a free slot, the recording is sent at once. When it
/// is full, a recording waits on disk until the window slides far enough to reach a free slot,
/// which happens at the publication pace, behind every recording already waiting. The replay is
/// recorded while that wait still leaves <see cref="SendSlack"/> before its candidate expiry
/// (for an ordinary replay, its game time plus
/// <see cref="ReplayMediaPolicySettings.OrdinaryCandidateMaxAge"/>, after which the uploader
/// deletes it as stale), and while the waiting recordings stay under one day of upload calls.
/// Uploads that only wait for their publish time need no disk and no upload; they count only
/// because they fill the window. A viewer request is always recorded.
/// </summary>
public static class RecordingCap
{
    public const string Requested = "cap-requested";
    public const string Off = "cap-off";
    public const string NotLive = "cap-not-live";
    public const string Room = "cap-room";
    public const string Queued = "cap-queued";
    public const string PublicationFull = "cap-publication-full";
    public const string UploadBacklog = "cap-upload-backlog";

    /// <summary>
    /// What a queued recording keeps in hand between its expected upload and its candidate
    /// expiry, for the pacing rules (map, rank, and hero cooldowns) that can pass one recording
    /// over for another, and for the match itself.
    /// </summary>
    public static readonly TimeSpan SendSlack = TimeSpan.FromDays(1);

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
            return new RecordingCapDecision(true, Requested, pending, ahead, -1);
        }

        if (!settings.CapRecordingToPublication)
        {
            return new RecordingCapDecision(true, Off, pending, ahead, -1);
        }

        // A dry run or a disabled uploader sends nothing, so nothing piles up for YouTube.
        if (!input.Live)
        {
            return new RecordingCapDecision(true, NotLive, pending, ahead, -1);
        }

        int uploads = Math.Max(0, input.UploadsPerDay);
        if (pending >= uploads)
        {
            return new RecordingCapDecision(false, UploadBacklog, pending, ahead, uploads);
        }

        if (!input.PublicListing)
        {
            return new RecordingCapDecision(true, Room, pending, ahead, uploads);
        }

        int capacity = PublicationCapacity(settings, uploads);

        // The slots that must open before this recording's: every one already taken or wanted,
        // and its own, less what the window holds.
        int overflow = pending + ahead + 1 - capacity;
        if (overflow <= 0)
        {
            return new RecordingCapDecision(
                true,
                Room,
                pending,
                ahead,
                capacity,
                TimeSpan.Zero,
                input.ExpiresAtUtc
            );
        }

        double perDay = PublishPerDay(settings, uploads);
        if (perDay <= 0 || input.ExpiresAtUtc is not DateTimeOffset expires)
        {
            return new RecordingCapDecision(
                false,
                PublicationFull,
                pending,
                ahead,
                capacity,
                null,
                input.ExpiresAtUtc
            );
        }

        TimeSpan wait = TimeSpan.FromDays(overflow / perDay);
        bool sentInTime = input.Now + wait + SendSlack <= expires;
        return new RecordingCapDecision(
            sentInTime,
            sentInTime ? Queued : PublicationFull,
            pending,
            ahead,
            capacity,
            wait,
            expires
        );
    }

    /// <summary>
    /// Videos that are not requests the pacing rules publish within MaxPublishAhead (at least
    /// one day), and no more than the upload calls of those days can send.
    /// </summary>
    public static int PublicationCapacity(ReplayMediaPolicySettings settings, int uploadsPerDay)
    {
        settings ??= new ReplayMediaPolicySettings();
        double days = Math.Max(1.0, settings.MaxPublishAhead.TotalDays);
        return (int)Math.Floor(PublishPerDay(settings, uploadsPerDay) * days);
    }

    /// <summary>
    /// The pace a full window drains at: the videos that are not requests the pacing rules
    /// publish in a day, and no more than a day of upload calls.
    /// </summary>
    private static double PublishPerDay(ReplayMediaPolicySettings settings, int uploadsPerDay) =>
        Math.Min(PublicationSchedule.OrdinaryPublishPerDay(settings), Math.Max(0, uploadsPerDay));

    /// <summary>Reads the pending recordings and the publication slots this decision needs.</summary>
    public static RecordingCapInput Measure(
        AppSettings settings,
        ReplayMediaDecision decision,
        DateTimeOffset now
    )
    {
        YouTubeSettings youtube = settings?.YouTube ?? new YouTubeSettings();
        ReplayMediaPolicySettings media = settings?.ReplayMedia ?? new ReplayMediaPolicySettings();
        string data = settings?.Location?.DataDirectory;
        string ledger = PublicationReservation.PathFor(data);
        return new RecordingCapInput
        {
            Priority = decision?.Priority ?? ReplayMediaPriority.Ordinary,
            PendingUploads = string.IsNullOrWhiteSpace(data)
                ? 0
                : NeedingSlot(settings, youtube, ledger, now),
            ScheduledAhead = PublicationReservation.CountAfter(ledger, now),
            Live = youtube.Enabled && !youtube.DryRun,
            PublicListing = YouTubeListing.IsPublic(youtube),
            UploadsPerDay = Math.Min(
                YouTubeQuotaUnits.UsableUploadCalls(youtube),
                Math.Max(0, media.MaxInsertsPerQuotaDay)
            ),
            ExpiresAtUtc = decision?.CandidateExpiresAtUtc is DateTime expires
                ? new DateTimeOffset(DateTime.SpecifyKind(expires, DateTimeKind.Utc))
                : null,
            Now = now,
        };
    }

    /// <summary>
    /// Recordings that wait for their upload and still need a slot. A send that started and
    /// failed keeps its slot, so that recording is already one of the slots after now.
    /// </summary>
    private static int NeedingSlot(
        AppSettings settings,
        YouTubeSettings youtube,
        string ledger,
        DateTimeOffset now
    )
    {
        int needing = 0;
        foreach (
            string recording in PendingUploadSize.Recordings(
                settings.ContextsDirectory,
                youtube.EntryFileName,
                youtube.EntryFileNameUploaded
            )
        )
        {
            if (!HoldsSlotAfter(recording, youtube.EntryFileName, ledger, now))
            {
                needing++;
            }
        }

        return needing;
    }

    private static bool HoldsSlotAfter(
        string recording,
        string entryFileName,
        string ledger,
        DateTimeOffset now
    )
    {
        string directory = Path.GetDirectoryName(recording);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(entryFileName))
        {
            return false;
        }

        YouTubeEntry entry = PendingYouTubeUpload.ReadEntry(Path.Combine(directory, entryFileName));
        if (entry?.ReplayId is not int replayId || replayId <= 0)
        {
            return false;
        }

        // The uploader's work key for a full match is replay-<id>, the slot it reserves.
        DateTimeOffset? slot = PublicationReservation.HeldAt(
            ledger,
            "replay-" + replayId.ToString(CultureInfo.InvariantCulture)
        );
        return slot > now;
    }
}
