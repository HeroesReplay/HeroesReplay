using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// The first videos.insert is private. A non-production host also keeps the [TEST] marker.
/// Public success is a later read-back, not the insert response.
/// </summary>
public static class UploadStaging
{
    public const string InitialPrivacy = "private";

    public static void Apply(YouTubeEntry entry, YouTubeSettings youtube, string hostName)
    {
        Apply(entry, youtube, hostName, null, null);
    }

    public static void Apply(
        YouTubeEntry entry,
        YouTubeSettings youtube,
        string hostName,
        DateTimeOffset? nowUtc,
        DateTimeOffset? lastPublicUtc
    )
    {
        YouTubeListing.StampForHost(entry, youtube, hostName);
        if (entry == null)
        {
            return;
        }

        string desiredFinal = string.IsNullOrWhiteSpace(entry.DesiredPrivacyStatus)
            ? entry.PrivacyStatus
            : entry.DesiredPrivacyStatus;
        if (!TwitchIngestGuard.IsProductionHost(hostName))
        {
            desiredFinal = UploadVisibility.Staged;
        }

        entry.PrivacyStatus = InitialPrivacy;
        if (entry.PublishAtUtc == null && nowUtc != null)
        {
            entry.PublishAtUtc = PublicationSchedule.NextPublishAt(
                desiredFinal,
                nowUtc.Value,
                lastPublicUtc
            );
        }
    }

    public static bool AllowsPublicReceipt(string actualPrivacy)
    {
        return string.Equals(actualPrivacy, "public", System.StringComparison.OrdinalIgnoreCase);
    }
}
