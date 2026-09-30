using System;

namespace HeroesReplay.Core.Services.YouTube;

/// <summary>
/// The first insert is private. A public receipt requires YouTube to report public.
/// </summary>
public static class UploadVisibility
{
    public const string Staged = "private";

    public static string InsertStatus(string desiredFinal)
    {
        _ = desiredFinal;
        return Staged;
    }

    public static bool CountsAsPublic(string actualStatus, string desiredFinal)
    {
        if (!string.Equals(actualStatus, "public", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(desiredFinal, "public", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The insert stays private. A public listing can carry the time YouTube should publish it.
    /// </summary>
    public static DateTimeOffset? PublishAt(string desiredFinal, DateTimeOffset? whenUtc)
    {
        if (!string.Equals(desiredFinal, "public", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (whenUtc == null || whenUtc.Value.Offset != TimeSpan.Zero)
        {
            return null;
        }

        return whenUtc;
    }

    /// <summary>
    /// True while the listing should become public and YouTube still reports something else.
    /// A private video id is not public.
    /// </summary>
    public static bool ReconcileUntilPublic(string actualStatus, string desiredFinal)
    {
        if (!string.Equals(desiredFinal, "public", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !CountsAsPublic(actualStatus, desiredFinal);
    }
}
