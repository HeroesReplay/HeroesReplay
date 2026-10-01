using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// The first videos.insert is private. A configuration that is not public
/// (<c>YouTube:PrivacyStatus</c>) also keeps the final listing private.
/// Public success is a later read-back, not the insert response.
/// </summary>
public static class UploadStaging
{
    public const string InitialPrivacy = "private";

    public static void Apply(YouTubeEntry entry, YouTubeSettings youtube)
    {
        Apply(entry, youtube, null, null);
    }

    public static void Apply(
        YouTubeEntry entry,
        YouTubeSettings youtube,
        DateTimeOffset? nowUtc,
        DateTimeOffset? lastPublicUtc,
        TimeSpan? minimumInterval = null
    )
    {
        YouTubeListing.Stamp(entry, youtube);
        if (entry == null)
        {
            return;
        }

        string desiredFinal = string.IsNullOrWhiteSpace(entry.DesiredPrivacyStatus)
            ? entry.PrivacyStatus
            : entry.DesiredPrivacyStatus;
        if (!YouTubeListing.IsPublic(youtube))
        {
            desiredFinal = UploadVisibility.Staged;
        }

        entry.PrivacyStatus = InitialPrivacy;
        if (entry.PublishAtUtc == null && nowUtc != null)
        {
            entry.PublishAtUtc = PublicationSchedule.NextPublishAt(
                desiredFinal,
                nowUtc.Value,
                lastPublicUtc,
                minimumInterval
            );
        }
    }

    public static bool AllowsPublicReceipt(string actualPrivacy)
    {
        return string.Equals(actualPrivacy, "public", System.StringComparison.OrdinalIgnoreCase);
    }
}
