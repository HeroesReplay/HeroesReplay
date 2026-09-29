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
        YouTubeListing.StampForHost(entry, youtube, hostName);
        if (entry != null)
        {
            entry.PrivacyStatus = InitialPrivacy;
        }
    }

    public static bool AllowsPublicReceipt(string actualPrivacy)
    {
        return string.Equals(actualPrivacy, "public", System.StringComparison.OrdinalIgnoreCase);
    }
}
