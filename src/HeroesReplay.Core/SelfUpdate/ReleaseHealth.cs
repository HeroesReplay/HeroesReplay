using System;
using System.Globalization;

namespace HeroesReplay.Core.SelfUpdate;

/// <summary>
/// The previous install stays until every role has been ready for the stabilization window.
/// A missing role file is not ready.
/// </summary>
public static class ReleaseHealth
{
    public static readonly TimeSpan StabilizeFor = TimeSpan.FromMinutes(2);

    public static bool MayDiscardPrevious(bool allRolesReady, TimeSpan healthyFor)
    {
        if (!allRolesReady || healthyFor < TimeSpan.Zero)
        {
            return false;
        }

        return healthyFor >= StabilizeFor;
    }

    public static string FormatRoleFile(DateTimeOffset sinceUtc)
    {
        return "roles=ready"
            + Environment.NewLine
            + "since="
            + sinceUtc.ToString("o", CultureInfo.InvariantCulture)
            + Environment.NewLine;
    }

    public static bool MayDiscardRoleFile(string text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text) || now.Offset != TimeSpan.Zero)
        {
            return false;
        }

        bool ready = false;
        DateTimeOffset? since = null;
        string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.StartsWith("roles=", StringComparison.Ordinal))
            {
                ready = string.Equals(
                    line.Substring("roles=".Length).Trim(),
                    "ready",
                    StringComparison.Ordinal
                );
            }
            else if (
                line.StartsWith("since=", StringComparison.Ordinal)
                && DateTimeOffset.TryParse(
                    line.Substring("since=".Length).Trim(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset parsed
                )
                && parsed.Offset == TimeSpan.Zero
            )
            {
                since = parsed;
            }
        }

        if (!ready || since == null || now < since.Value)
        {
            return false;
        }

        return MayDiscardPrevious(true, now - since.Value);
    }
}
