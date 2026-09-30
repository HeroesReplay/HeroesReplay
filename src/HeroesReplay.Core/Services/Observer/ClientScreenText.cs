using System;

namespace HeroesReplay.Core.Services.Observer;

public static class ClientScreenText
{
    public const string GameDataStartup = "Preparing game data";
    public const string GameDataDownload = "must be fully downloaded";

    public static bool IsGameDataStartup(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains(GameDataStartup, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGameDataDownload(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains(GameDataDownload, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One wait reads the main window and can read another window before it decides.
    /// The download dialog counts when either sample shows it.
    /// </summary>
    public static bool IsGameDataDownload(string primary, string later)
    {
        return IsGameDataDownload(primary) || IsGameDataDownload(later);
    }

    public static bool IsGameDataStartup(string primary, string later)
    {
        return IsGameDataStartup(primary) || IsGameDataStartup(later);
    }

    public static bool IsLoginForm(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        bool password = text.Contains("password", StringComparison.OrdinalIgnoreCase);
        bool email = text.Contains("email", StringComparison.OrdinalIgnoreCase);
        bool logIn =
            text.Contains("log in", StringComparison.OrdinalIgnoreCase)
            || text.Contains("login", StringComparison.OrdinalIgnoreCase);
        return password && (email || logIn);
    }

    public static bool IsVersionMismatch(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains("version mismatch", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRegionUnavailable(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("region", StringComparison.OrdinalIgnoreCase)
            && text.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }
}
