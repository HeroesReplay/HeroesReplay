using System;
using System.Globalization;
using HeroesReplay.Core.YouTube.Publication;

namespace HeroesReplay.Core.YouTube;

public sealed class YouTubeUploaderHealthInput
{
    /// <summary>YouTube is enabled and not a dry run, so recordings really go to YouTube.</summary>
    public bool Live { get; init; }

    /// <summary><c>YouTube:PrivacyStatus</c> is public, so uploads are meant to go public.</summary>
    public bool PublicListing { get; init; }

    /// <summary>Recordings on disk that still wait for their <c>videos.insert</c>.</summary>
    public int Pending { get; init; }

    /// <summary>The upload bucket or a quota pause holds new uploads.</summary>
    public bool QuotaBlocked { get; init; }

    /// <summary>
    /// <c>ReplayMedia:MaxInsertsPerQuotaDay</c> inserts were sent this Pacific quota day: this
    /// app's own cap holds new uploads, not YouTube.
    /// </summary>
    public bool InsertCapped { get; init; }

    public int InsertCap { get; init; }

    /// <summary>
    /// Why the last library pass did not run, when it was not a routine skip (for example no
    /// library consent). The library pass is what confirms a scheduled upload public.
    /// </summary>
    public string LibraryProblem { get; init; }

    public DateTimeOffset? UploadsResumeAt { get; init; }

    public PublicationTallyReport Tally { get; init; }

    public DateTimeOffset Now { get; init; }
}

public sealed record YouTubeUploaderConcern(string Code, string Cause);

/// <summary>
/// When <c>services status</c> shows the uploader degraded (#250): uploads are blocked by quota
/// while recordings wait, or nothing went public for a day while uploads are past their
/// publish time. Null means healthy.
/// </summary>
public static class YouTubeUploaderHealth
{
    public const string QuotaBlockedCode = "youtube.quota_blocked";
    public const string NotPublishingCode = "youtube.not_publishing";

    public static readonly TimeSpan PublishGap = TimeSpan.FromHours(24);

    public static YouTubeUploaderConcern Evaluate(YouTubeUploaderHealthInput input)
    {
        if (input == null || !input.Live)
        {
            return null;
        }

        if ((input.QuotaBlocked || input.InsertCapped) && input.Pending > 0)
        {
            string resume = input.UploadsResumeAt is DateTimeOffset at
                ? " Uploads resume at " + at.ToString("u", CultureInfo.InvariantCulture) + "."
                : string.Empty;
            string holder = input.QuotaBlocked
                ? "the YouTube upload quota holds new uploads"
                : $"this app's daily insert cap holds new uploads (ReplayMedia:MaxInsertsPerQuotaDay {input.InsertCap} were sent this Pacific quota day)";
            return new YouTubeUploaderConcern(
                QuotaBlockedCode,
                $"{input.Pending} recording(s) wait for upload and {holder}.{resume}"
            );
        }

        PublicationTallyReport tally = input.Tally;
        if (!input.PublicListing || tally == null || tally.Due <= 0)
        {
            return null;
        }

        if (tally.LastPublicAt is DateTimeOffset last && input.Now - last <= PublishGap)
        {
            return null;
        }

        string library = string.IsNullOrWhiteSpace(input.LibraryProblem)
            ? string.Empty
            : " The last library pass was skipped: " + input.LibraryProblem + ".";
        string lastText = tally.LastPublicAt is DateTimeOffset seen
            ? "since " + seen.ToString("u", CultureInfo.InvariantCulture)
            : "ever";
        return new YouTubeUploaderConcern(
            NotPublishingCode,
            $"No video was confirmed public {lastText}, while {tally.Due} upload(s) are past their publish time ({tally.StuckPrivate} more than the grace). The library pass confirms privacy; check that it runs, and that the API project is not locked to private uploads.{library}"
        );
    }
}
