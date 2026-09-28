using System;
using System.Linq;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// The HUD clock is only -MM:SS before the gates, or MM:SS after. Games stay under 90 minutes.
/// </summary>
public static class HudClock
{
    public static bool TryParse(string text, out TimeSpan clock)
    {
        clock = default;
        string time = Sanitize(text);
        if (!System.Text.RegularExpressions.Regex.IsMatch(time, @"^-?\d{1,2}:\d{2}$"))
        {
            return false;
        }

        bool negative = time.StartsWith('-');
        string[] segments = (negative ? time[1..] : time).Split(':');
        if (
            !int.TryParse(segments[0], out int minutes)
            || !int.TryParse(segments[1], out int seconds)
            || minutes > 90
            || seconds > 59
        )
        {
            return false;
        }

        clock = new TimeSpan(0, minutes, seconds);
        if (negative)
        {
            clock = clock.Negate();
        }

        return true;
    }

    public static string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return new string(
            text.Replace("O", "0", StringComparison.OrdinalIgnoreCase)
                .Replace("L", "1", StringComparison.OrdinalIgnoreCase)
                .Replace("Z", "2", StringComparison.OrdinalIgnoreCase)
                .Replace("E", "3", StringComparison.OrdinalIgnoreCase)
                .Replace("A", "4", StringComparison.OrdinalIgnoreCase)
                .Replace("S", "5", StringComparison.OrdinalIgnoreCase)
                .Replace("G", "6", StringComparison.OrdinalIgnoreCase)
                .Replace("T", "7", StringComparison.OrdinalIgnoreCase)
                .Replace("B", "8", StringComparison.OrdinalIgnoreCase)
                .Replace(".", ":", StringComparison.OrdinalIgnoreCase)
                .Replace("'", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("\"", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Where(c => char.IsDigit(c) || c.Equals(':') || c.Equals('-'))
                .ToArray()
        );
    }
}
