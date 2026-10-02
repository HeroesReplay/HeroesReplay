using System;
using System.Collections.Generic;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Playlists;

/// <summary>
/// Playlist titles for each group. A title is the playlist's key, so the same facts always
/// give the same title, and a title is never longer than YouTube allows.
/// </summary>
public static class YouTubePlaylistNames
{
    /// <summary>YouTube's limit for a playlist title.</summary>
    public const int MaxTitleLength = 150;

    public const string ViewerReviews = "Viewer requested reviews";
    public const string DraftPrefix = "Unusual drafts";

    private const string StormLeague = "Storm League";

    /// <summary>
    /// The map group: the English catalog name, or null for a map the catalog does not have.
    /// </summary>
    public static string Map(string map)
    {
        string canonical = EnglishMapNames.Canonical(map);
        return EnglishMapNames.IsCatalog(canonical) ? Fit(canonical) : null;
    }

    /// <summary>
    /// The rank group: <c>Storm League - Diamond</c>. Only a Storm League game with a known
    /// league has one.
    /// </summary>
    public static string Rank(string gameType, string rank)
    {
        if (!string.Equals(Mode(gameType), StormLeague, StringComparison.Ordinal))
        {
            return null;
        }

        string league = League(rank);
        return league == null ? null : StormLeague + " - " + league;
    }

    /// <summary>
    /// The draft group: one title per note. <c>Blue no tank, Red double healer</c> is two
    /// notes, <c>No tank</c> and <c>Double healer</c>, so each team's note has one playlist.
    /// </summary>
    public static IReadOnlyList<string> Drafts(string draft)
    {
        var titles = new List<string>();
        foreach (string note in DraftNotes(draft))
        {
            string title = Fit(DraftPrefix + " - " + note);
            if (title != null && !titles.Contains(title))
            {
                titles.Add(title);
            }
        }

        return titles;
    }

    /// <summary>
    /// The notes in a <c>Draft:</c> line without the team names, first letter upper case.
    /// </summary>
    public static IReadOnlyList<string> DraftNotes(string draft)
    {
        var notes = new List<string>();
        if (string.IsNullOrWhiteSpace(draft))
        {
            return notes;
        }

        foreach (
            string part in draft.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            string note = WithoutTeam(part);
            if (note.Length == 0)
            {
                continue;
            }

            note = char.ToUpperInvariant(note[0]) + note.Substring(1);
            if (!notes.Contains(note))
            {
                notes.Add(note);
            }
        }

        return notes;
    }

    /// <summary>
    /// The earlier combined group: <c>Alterac Pass - Storm League - Diamond</c>, or
    /// <c>Alterac Pass - Quick Match</c>.
    /// </summary>
    public static string Title(string map, string gameType, string rank)
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return null;
        }

        string mode = Mode(gameType);
        if (mode == null)
        {
            return null;
        }

        map = map.Trim();
        if (mode == StormLeague)
        {
            string league = League(rank);
            return Fit(
                league == null ? map + " - " + StormLeague : map + " - " + mode + " - " + league
            );
        }

        return Fit(map + " - " + mode);
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

    /// <summary>
    /// The playlist name of a game mode, or null for a mode that is not filed.
    /// </summary>
    public static string Mode(string gameType)
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

        if (trimmed.Equals(StormLeague, StringComparison.OrdinalIgnoreCase))
        {
            return StormLeague;
        }

        return null;
    }

    /// <summary>
    /// A title YouTube accepts: no angle brackets, at most <see cref="MaxTitleLength"/>
    /// characters. Null when nothing is left.
    /// </summary>
    public static string Fit(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string clean = title.Replace("<", string.Empty).Replace(">", string.Empty).Trim();
        if (clean.Length > MaxTitleLength)
        {
            clean = clean.Substring(0, MaxTitleLength).TrimEnd();
        }

        return clean.Length == 0 ? null : clean;
    }

    private static string WithoutTeam(string part)
    {
        foreach (string team in new[] { "Blue ", "Red " })
        {
            if (part.StartsWith(team, StringComparison.Ordinal))
            {
                return part.Substring(team.Length).Trim();
            }
        }

        return part.Trim();
    }

    private static bool IsLeague(string rank, string league) =>
        rank.Equals(league, StringComparison.OrdinalIgnoreCase)
        || rank.StartsWith(league + " ", StringComparison.OrdinalIgnoreCase);
}
