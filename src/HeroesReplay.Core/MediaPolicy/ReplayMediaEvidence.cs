using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Clips;
using HeroesReplay.Core.Obs;

namespace HeroesReplay.Core.MediaPolicy;

/// <summary>
/// Pentakills are clips <see cref="TeamKillClips"/> already built from <see cref="KillStreaks"/>.
/// A team wipe is one of them (<see cref="TeamKillClip.WipedTeam"/>), not an event of its own
/// (#369). Unknown kinds are not evidence.
/// </summary>
public static class ReplayMediaEvidence
{
    public static TeamKillClip[] Accepted(IReadOnlyList<TeamKillClip> events)
    {
        if (events == null || events.Count == 0)
        {
            return Array.Empty<TeamKillClip>();
        }

        var kept = new List<TeamKillClip>(events.Count);
        foreach (TeamKillClip clip in events)
        {
            if (clip.Kind == TeamKillClips.PentakillKind)
            {
                kept.Add(clip);
            }
        }

        kept.Sort(Compare);
        return kept.ToArray();
    }

    /// <summary>A pentakill in <paramref name="events"/> killed the whole enemy team.</summary>
    public static bool AnyWipedTeam(IReadOnlyList<TeamKillClip> events)
    {
        if (events == null)
        {
            return false;
        }

        foreach (TeamKillClip clip in events)
        {
            if (clip.Kind == TeamKillClips.PentakillKind && clip.WipedTeam)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasKind(IReadOnlyList<TeamKillClip> events, string kind)
    {
        if (events == null || kind == null)
        {
            return false;
        }

        foreach (TeamKillClip clip in events)
        {
            if (clip.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    public static string FirstHero(IReadOnlyList<TeamKillClip> events, string kind)
    {
        if (events == null || kind == null)
        {
            return null;
        }

        foreach (TeamKillClip clip in events)
        {
            if (clip.Kind == kind && !string.IsNullOrWhiteSpace(clip.Hero))
            {
                return clip.Hero.Trim();
            }
        }

        return null;
    }

    private static int Compare(TeamKillClip left, TeamKillClip right)
    {
        int byStart = left.HudStartSecond.CompareTo(right.HudStartSecond);
        if (byStart != 0)
        {
            return byStart;
        }

        int byKind = string.CompareOrdinal(left.Kind, right.Kind);
        if (byKind != 0)
        {
            return byKind;
        }

        return string.CompareOrdinal(left.Hero, right.Hero);
    }
}

public static class ReplayMediaRanks
{
    public static int? Index(string rank)
    {
        string normalized = RankImage.Normalize(rank, null);
        switch (normalized)
        {
            case "bronze":
                return 0;
            case "silver":
                return 1;
            case "gold":
                return 2;
            case "platinum":
                return 3;
            case "diamond":
                return 4;
            case "master":
                return 5;
            case "grandmaster":
                return 6;
            default:
                return null;
        }
    }

    public static string Display(string rank)
    {
        switch (Index(rank))
        {
            case 0:
                return "Bronze";
            case 1:
                return "Silver";
            case 2:
                return "Gold";
            case 3:
                return "Platinum";
            case 4:
                return "Diamond";
            case 5:
                return "Master";
            case 6:
                return "Grandmaster";
            default:
                return null;
        }
    }
}
