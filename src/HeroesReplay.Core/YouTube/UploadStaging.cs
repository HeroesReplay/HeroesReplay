using System;
using HeroesReplay.Core.YouTube.Metadata;

namespace HeroesReplay.Core.YouTube;

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
        YouTubeListing.Stamp(entry, youtube);
        if (entry == null)
        {
            return;
        }

        entry.PrivacyStatus = InitialPrivacy;
    }

    /// <summary>
    /// Stores the reserved slot as the time YouTube should publish the video. Only a public
    /// listing whose entry wants to be public gets a time. Anything else stays private.
    /// </summary>
    public static void Schedule(
        YouTubeEntry entry,
        YouTubeSettings youtube,
        DateTimeOffset? publishAtUtc
    )
    {
        if (entry == null)
        {
            return;
        }

        entry.PublishAtUtc = YouTubeListing.IsPublic(youtube)
            ? UploadVisibility.PublishAt(entry.DesiredPrivacyStatus, publishAtUtc)
            : null;
    }

    public static bool AllowsPublicReceipt(string actualPrivacy)
    {
        return string.Equals(actualPrivacy, "public", StringComparison.OrdinalIgnoreCase);
    }
}
