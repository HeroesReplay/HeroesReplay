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
}
