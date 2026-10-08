using System;
using System.Collections.Generic;
using System.Globalization;
using HeroesReplay.Core.Analysis;

namespace HeroesReplay.Core.Clips;

public readonly record struct TeamKillDeath(
    int Second,
    string KillerHero,
    string VictimHero,
    int KillerKey = -1
);

public readonly record struct TeamKillBlow(int Second, string Victim);

public readonly record struct TeamKillClip(
    string Kind,
    string Hero,
    int FirstDeathSecond,
    int LastDeathSecond,
    int HudStartSecond,
    int HudEndSecond,
    string Description,
    IReadOnlyList<TeamKillBlow> Kills = null
)
{
    /// <summary>
    /// The pentakill's blows killed five different enemy heroes: the whole team. It is a fact
    /// about this one clip, which the media score and the full match tags use. It is never a
    /// clip of its own (#369).
    /// </summary>
    public bool WipedTeam => TeamKillClips.UniqueVictims(Kills) >= 5;
}

/// <summary>
/// Individual pentakills: one player lands five or more hero-unit killing blows, each within the
/// window of the previous one. Only they are clips, so clips stay rare. A wipe shared across
/// players is never a clip, and a pentakill that killed the whole team is still one clip
/// (<see cref="TeamKillClip.WipedTeam"/>, #369).
/// </summary>
public static class TeamKillClips
{
    public const string PentakillKind = "pentakill";

    public static IReadOnlyList<TeamKillClip> Select(
        IReadOnlyList<TeamKillDeath> deaths,
        int windowSeconds = 12,
        int leadSeconds = 12,
        int tailSeconds = 8
    )
    {
        if (deaths == null || deaths.Count == 0)
        {
            return Array.Empty<TeamKillClip>();
        }

        if (windowSeconds <= 0)
        {
            windowSeconds = 12;
        }

        if (leadSeconds < 0)
        {
            leadSeconds = 0;
        }

        if (tailSeconds < 0)
        {
            tailSeconds = 0;
        }

        var clips = new List<TeamKillClip>();
        clips.AddRange(ByKiller(deaths, windowSeconds, leadSeconds, tailSeconds));
        clips.Sort(
            (left, right) =>
            {
                int byStart = left.HudStartSecond.CompareTo(right.HudStartSecond);
                if (byStart != 0)
                {
                    return byStart;
                }

                return string.CompareOrdinal(left.Hero, right.Hero);
            }
        );
        return clips;
    }

    private static List<TeamKillClip> ByKiller(
        IReadOnlyList<TeamKillDeath> deaths,
        int windowSeconds,
        int leadSeconds,
        int tailSeconds
    )
    {
        var byKiller = new Dictionary<string, List<TeamKillDeath>>(StringComparer.Ordinal);
        foreach (TeamKillDeath death in deaths)
        {
            if (
                string.IsNullOrWhiteSpace(death.KillerHero)
                || string.IsNullOrWhiteSpace(death.VictimHero)
            )
            {
                continue;
            }

            string key = Identity(death);
            if (!byKiller.TryGetValue(key, out List<TeamKillDeath> list))
            {
                list = new List<TeamKillDeath>();
                byKiller[key] = list;
            }

            list.Add(death);
        }

        var clips = new List<TeamKillClip>();
        foreach (List<TeamKillDeath> victims in byKiller.Values)
        {
            victims.Sort((left, right) => left.Second.CompareTo(right.Second));
            string hero = victims[0].KillerHero;
            var seconds = new List<int>(victims.Count);
            foreach (TeamKillDeath victim in victims)
            {
                seconds.Add(victim.Second);
            }

            IReadOnlyList<KillStreak> streaks = KillStreaks.Group(seconds, windowSeconds);
            int offset = 0;
            foreach (KillStreak streak in streaks)
            {
                if (streak.Kills >= 5)
                {
                    // One clip per pentakill, also when its victims were the whole team (#369).
                    TeamKillBlow[] blows = Blows(victims, offset, streak.Kills);
                    clips.Add(
                        Clip(
                            PentakillKind,
                            hero,
                            blows[0].Second,
                            blows[blows.Length - 1].Second,
                            leadSeconds,
                            tailSeconds,
                            UniqueVictims(blows) >= 5
                                ? hero + " pentakill (team wipe)"
                                : hero + " pentakill",
                            blows
                        )
                    );
                }

                offset += streak.Kills;
            }
        }

        return clips;
    }

    private static string Identity(TeamKillDeath death)
    {
        if (death.KillerKey >= 0)
        {
            return death.KillerKey.ToString(CultureInfo.InvariantCulture);
        }

        return death.KillerHero ?? string.Empty;
    }

    private static TeamKillBlow[] Blows(List<TeamKillDeath> deaths, int offset, int count)
    {
        var blows = new TeamKillBlow[count];
        for (int i = 0; i < count; i++)
        {
            TeamKillDeath death = deaths[offset + i];
            blows[i] = new TeamKillBlow(death.Second, death.VictimHero);
        }

        return blows;
    }

    internal static int UniqueVictims(IReadOnlyList<TeamKillBlow> blows)
    {
        if (blows == null)
        {
            return 0;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TeamKillBlow blow in blows)
        {
            if (!string.IsNullOrWhiteSpace(blow.Victim))
            {
                seen.Add(blow.Victim);
            }
        }

        return seen.Count;
    }

    private static TeamKillClip Clip(
        string kind,
        string hero,
        int firstDeath,
        int lastDeath,
        int leadSeconds,
        int tailSeconds,
        string description,
        IReadOnlyList<TeamKillBlow> kills
    )
    {
        int start = firstDeath - leadSeconds;
        if (start < 0)
        {
            start = 0;
        }

        return new TeamKillClip(
            kind,
            hero ?? string.Empty,
            firstDeath,
            lastDeath,
            start,
            lastDeath + tailSeconds,
            description,
            kills
        );
    }
}
