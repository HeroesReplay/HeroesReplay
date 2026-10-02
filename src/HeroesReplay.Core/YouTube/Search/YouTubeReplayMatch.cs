using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.YouTube.Search;

public static class YouTubeReplayMatch
{
    private static readonly Regex DescriptionId = new(
        @"(?:replayID=|Replay ID:\s*)(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public static bool Mentions(string text, int replayId)
    {
        if (string.IsNullOrEmpty(text) || replayId <= 0)
        {
            return false;
        }

        string id = replayId.ToString();
        int index = 0;
        while ((index = text.IndexOf(id, index, StringComparison.Ordinal)) >= 0)
        {
            bool left = index == 0 || !char.IsDigit(text[index - 1]);
            int end = index + id.Length;
            bool right = end >= text.Length || !char.IsDigit(text[end]);
            if (left && right)
            {
                return true;
            }

            index = end;
        }

        return false;
    }

    /// <summary>
    /// Replay ids an uploaded video names: the title's id part, and the description's
    /// <c>Replay ID:</c> line or Heroes Profile <c>replayID=</c> link.
    /// </summary>
    public static IEnumerable<int> IdsIn(string title, string description)
    {
        if (TryReadTitleId(title, out int titleId))
        {
            yield return titleId;
        }

        if (string.IsNullOrEmpty(description))
        {
            yield break;
        }

        foreach (Match match in DescriptionId.Matches(description))
        {
            if (
                int.TryParse(
                    match.Groups[1].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int id
                )
                && id > 0
            )
            {
                yield return id;
            }
        }
    }

    public static int? FromEntry(YouTubeEntry entry)
    {
        if (entry?.ReplayId is > 0)
        {
            return entry.ReplayId;
        }

        if (TryReadTitleId(entry?.Title, out int titleId))
        {
            return titleId;
        }

        return null;
    }

    public static bool TryReadTitleId(string title, out int replayId)
    {
        replayId = 0;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        foreach (string part in title.Split(" - ", StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int id) && id > 0)
            {
                replayId = id;
                return true;
            }
        }

        return false;
    }
}
