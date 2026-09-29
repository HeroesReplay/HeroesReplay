using System.Text.RegularExpressions;

namespace HeroesReplay.Core.Services.Client;

/// <summary>
/// Battle.net draws Play and Update inside its window. The words come from OCR.
/// </summary>
public static class LauncherButtonText
{
    public static string Classify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "unreadable";
        }

        if (Regex.IsMatch(text, @"\bUpdating\b", RegexOptions.IgnoreCase))
        {
            return "Updating";
        }

        if (Regex.IsMatch(text, @"\bUpdate\b", RegexOptions.IgnoreCase))
        {
            return "Update";
        }

        if (Regex.IsMatch(text, @"\bPlay\b", RegexOptions.IgnoreCase))
        {
            return "Play";
        }

        return "unknown";
    }
}
