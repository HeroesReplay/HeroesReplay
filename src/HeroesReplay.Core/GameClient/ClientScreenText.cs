using System;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// The text rules left from the client's OCR (#292): Battle.net's own dialogs, which are not
/// game-launch results and which no live run can reproduce. "The selected region is currently
/// unavailable. Please try again later or select another region." and "Game client version mismatch
/// with selected region. ..." are Battle.net authentication errors from the client's error table,
/// shown in its Battle.net error dialog. The email and password form's words keep the disconnect
/// rule off that form (#385). Every client screen comes from memory or the client's windows.
/// </summary>
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

    /// <summary>Battle.net's "Game client version mismatch with selected region." error.</summary>
    public static bool IsVersionMismatch(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains("version mismatch", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Battle.net's "The selected region is currently unavailable." error.</summary>
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
