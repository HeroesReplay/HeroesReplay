using System;

namespace HeroesReplay.Core.Services.Observer;

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
