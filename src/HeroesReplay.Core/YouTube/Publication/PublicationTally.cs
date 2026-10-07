using System;
using System.Collections.Generic;
using HeroesReplay.Core.YouTube.Playlists;

namespace HeroesReplay.Core.YouTube.Publication;

public sealed class PublicationTallyReport
{
    /// <summary>Videos YouTube reported public whose publish time is in the last 24 hours.</summary>
    public int PublishedDay { get; init; }

    /// <summary>Videos YouTube reported public whose publish time is in the last 7 days.</summary>
    public int PublishedWeek { get; init; }

    /// <summary>
    /// Uploads that are not public yet and are not late: their publish time is ahead, or it
    /// passed less than the confirmation grace ago and the library pass has not looked yet.
    /// </summary>
    public int Scheduled { get; init; }

    /// <summary>Uploads still reported private longer than the grace after their publish time.</summary>
    public int StuckPrivate { get; init; }

    /// <summary>Uploads whose publish time has passed and that the record does not show public.</summary>
    public int Due { get; init; }

    /// <summary>The newest publish time of a video reported public. Null when none is.</summary>
    public DateTimeOffset? LastPublicAt { get; init; }

    public IReadOnlyList<string> StuckVideoIds { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Publication counts from <c>Data\youtube-library.jsonl</c>, the record the uploader appends at
/// each insert and the library pass updates when YouTube reports a scheduled video public.
/// The uploader's own ledger cannot count these: every insert is private with a
/// <c>publishAt</c>, so the insert response is never public (#250).
/// </summary>
public static class PublicationTally
{
    /// <summary>A late video older than this is no longer checked by the library pass.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromDays(30);

    private static readonly TimeSpan Day = TimeSpan.FromHours(24);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>
    /// How long after its publish time a video may still read private in the record: two
    /// library passes, so one pass that was skipped or throttled is not a stuck video.
    /// </summary>
    public static TimeSpan ConfirmGrace(TimeSpan libraryInterval)
    {
        TimeSpan twice = libraryInterval > TimeSpan.Zero ? libraryInterval * 2 : TimeSpan.Zero;
        return twice > TimeSpan.FromHours(2) ? twice : TimeSpan.FromHours(2);
    }

    public static PublicationTallyReport Count(
        IEnumerable<YouTubeLibraryVideo> videos,
        DateTimeOffset now,
        TimeSpan grace
    )
    {
        int day = 0;
        int week = 0;
        int scheduled = 0;
        int due = 0;
        DateTimeOffset? lastPublic = null;
        var stuck = new List<string>();
        foreach (YouTubeLibraryVideo video in videos ?? Array.Empty<YouTubeLibraryVideo>())
        {
            if (video == null || string.IsNullOrWhiteSpace(video.VideoId))
            {
                continue;
            }

            if (string.Equals(video.PrivacyStatus, "public", StringComparison.OrdinalIgnoreCase))
            {
                DateTimeOffset? at = video.PublishAt ?? video.UploadedAt;
                if (at is not DateTimeOffset when || when > now)
                {
                    continue;
                }

                if (lastPublic == null || when > lastPublic.Value)
                {
                    lastPublic = when;
                }

                if (now - when < Day)
                {
                    day++;
                }

                if (now - when < Week)
                {
                    week++;
                }

                continue;
            }

            // A private listing (dev, [TEST]) has no publish time and is meant to stay private.
            if (video.PublishAt is not DateTimeOffset publishAt)
            {
                continue;
            }

            if (publishAt > now)
            {
                scheduled++;
                continue;
            }

            if (now - publishAt > Lookback)
            {
                continue;
            }

            due++;
            if (now - publishAt <= grace)
            {
                scheduled++;
            }
            else
            {
                stuck.Add(video.VideoId.Trim());
            }
        }

        return new PublicationTallyReport
        {
            PublishedDay = day,
            PublishedWeek = week,
            Scheduled = scheduled,
            StuckPrivate = stuck.Count,
            Due = due,
            LastPublicAt = lastPublic,
            StuckVideoIds = stuck,
        };
    }
}
