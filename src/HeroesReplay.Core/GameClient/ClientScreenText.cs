using System;

namespace HeroesReplay.Core.GameClient;

public static class ClientScreenText
{
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
            && (
                text.Contains("version mismatch", StringComparison.OrdinalIgnoreCase)
                || IsVersionNotAvailable(text)
            );
    }

    /// <summary>
    /// "The version of Heroes of the Storm required to play this game is not available." Blizzard
    /// no longer serves the replay's build, so that client will never arrive.
    /// </summary>
    public static bool IsVersionNotAvailable(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains("version of Heroes", StringComparison.OrdinalIgnoreCase)
            && text.Contains("not available", StringComparison.OrdinalIgnoreCase);
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
