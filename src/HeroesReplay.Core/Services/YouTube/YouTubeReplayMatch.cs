using System;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.YouTube;

public static class YouTubeReplayMatch
{
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
