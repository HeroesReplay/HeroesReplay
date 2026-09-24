using System;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.YouTube;

public static class YouTubePlaylistNames
{
    public static string For(YouTubeEntry entry)
    {
        if (entry == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(entry.Map) || !string.IsNullOrWhiteSpace(entry.GameType))
        {
            return Title(entry.Map, entry.GameType, entry.Rank);
        }

        return FromTitle(entry.Title);
    }

    public static string Title(string map, string gameType, string rank)
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return null;
        }

        string mode = CanonicalMode(gameType);
        if (mode == null)
        {
            return null;
        }

        map = map.Trim();
        if (mode == "Storm League")
        {
            string league = League(rank);
            return league == null ? map + " - Storm League" : map + " - Storm League - " + league;
        }

        return map + " - " + mode;
    }

    public static string League(string rank)
    {
        if (string.IsNullOrWhiteSpace(rank))
        {
            return null;
        }

        string trimmed = rank.Trim();
        if (IsLeague(trimmed, "Grandmaster") || IsLeague(trimmed, "Grand Master"))
        {
            return "Grandmaster";
        }

        string[] leagues = { "Master", "Diamond", "Platinum", "Gold", "Silver", "Bronze" };
        foreach (string league in leagues)
        {
            if (IsLeague(trimmed, league))
            {
                return league;
            }
        }

        return null;
    }

    public static string FromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string[] parts = title.Split(" - ", StringSplitOptions.None);
        if (parts.Length < 3 || !int.TryParse(parts[1].Trim(), out _))
        {
            return null;
        }

        string rank =
            parts.Length >= 4 ? string.Join(" - ", parts, 3, parts.Length - 3).Trim() : null;
        return Title(parts[0], parts[2], rank);
    }

    private static string CanonicalMode(string gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType))
        {
            return null;
        }

        string trimmed = gameType.Trim();
        if (trimmed.Equals("Quick Match", StringComparison.OrdinalIgnoreCase))
        {
            return "Quick Match";
        }

        if (trimmed.Equals("ARAM", StringComparison.OrdinalIgnoreCase))
        {
            return "ARAM";
        }

        if (trimmed.Equals("Unranked Draft", StringComparison.OrdinalIgnoreCase))
        {
            return "Unranked Draft";
        }

        if (trimmed.Equals("Storm League", StringComparison.OrdinalIgnoreCase))
        {
            return "Storm League";
        }

        return null;
    }

    private static bool IsLeague(string rank, string league) =>
        rank.Equals(league, StringComparison.OrdinalIgnoreCase)
        || rank.StartsWith(league + " ", StringComparison.OrdinalIgnoreCase);
}
