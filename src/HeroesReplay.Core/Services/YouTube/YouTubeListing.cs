using System;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// The title marker and the listing come from configuration, not the machine name.
/// <c>appsettings.dev.json</c> sets <c>TitlePrefix</c> [TEST] and <c>PrivacyStatus</c>
/// private so prelive listings can be deleted later. <c>appsettings.prod.json</c> is public.
/// </summary>
public static class YouTubeListing
{
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

    /// <summary>
    /// True when <c>YouTube:PrivacyStatus</c> is public, which is also the default when unset.
    /// Only a public listing is paced by the publication budgets.
    /// </summary>
    public static bool IsPublic(YouTubeSettings youtube)
    {
        string privacy = youtube?.PrivacyStatus;
        return string.IsNullOrWhiteSpace(privacy)
            || string.Equals(privacy.Trim(), "public", StringComparison.OrdinalIgnoreCase);
    }

    public static void Stamp(YouTubeEntry entry, YouTubeSettings youtube)
    {
        if (entry == null)
        {
            return;
        }

        entry.Title = ApplyMarker(entry.Title, youtube?.TitlePrefix);
        if (!IsPublic(youtube))
        {
            entry.PrivacyStatus = youtube.PrivacyStatus.Trim();
        }
    }
}
