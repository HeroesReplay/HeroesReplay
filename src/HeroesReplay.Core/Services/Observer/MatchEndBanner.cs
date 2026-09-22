using System;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// The award screen says MVP even when the parsed core time is later than the HUD clock.
/// Victory and defeat words also appear on camp tooltips, so those only count near the core.
/// </summary>
public static class MatchEndBanner
{
    public static bool IsEnd(string text, bool nearCore)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Garden Terror camp tooltip: "Defeat or bribe this camp to gain Mercenaries".
        if (
            text.Contains("bribe", StringComparison.OrdinalIgnoreCase)
            || text.Contains("mercenar", StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        if (Regex.IsMatch(text, @"\bMVP\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (!nearCore)
        {
            return false;
        }

        return Regex.IsMatch(text, @"\b(VICTORY|DEFEAT)\b", RegexOptions.IgnoreCase);
    }
}
