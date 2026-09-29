using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// Prelive uploads on any machine other than the production host stay private and
/// carry a [TEST] marker so those listings can be deleted later.
/// </summary>
public static class YouTubeListing
{
    public const string PreliveMarker = "[TEST]";

    public static string ApplyMarker(string title, string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return title ?? string.Empty;
        }

        string marker = prefix.Trim();
        string body = (title ?? string.Empty).Trim();
        if (body.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        return string.IsNullOrEmpty(body) ? marker : marker + " " + body;
    }

    public static void StampForHost(YouTubeEntry entry, YouTubeSettings youtube, string hostName)
    {
        if (entry == null)
        {
            return;
        }

        if (TwitchIngestGuard.IsProductionHost(hostName))
        {
            entry.Title = ApplyMarker(entry.Title, youtube?.TitlePrefix);
            return;
        }

        string marker = string.IsNullOrWhiteSpace(youtube?.TitlePrefix)
            ? PreliveMarker
            : youtube.TitlePrefix;
        entry.Title = ApplyMarker(entry.Title, marker);
        entry.PrivacyStatus = "private";
    }
}
