using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Analysis;

public readonly record struct TeamKillDeath(int Second, string KillerHero, string VictimHero);

public readonly record struct TeamKillClip(
    string Kind,
    string Hero,
    int FirstDeathSecond,
    int LastDeathSecond,
    int HudStartSecond,
    int HudEndSecond,
    string Description
);

/// <summary>
/// Pentakill and team-wipe intervals on the match clock, before a recording exists.
/// </summary>
public static class TeamKillClips
{
    public const string PentakillKind = "pentakill";
    public const string TeamWipeKind = "team-wipe";

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
        clips.AddRange(Pentakills(deaths, windowSeconds, leadSeconds, tailSeconds));
        clips.AddRange(TeamWipes(deaths, windowSeconds, leadSeconds, tailSeconds));
        clips.Sort(
            (left, right) =>
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
        );
        return clips;
    }

    private static List<TeamKillClip> Pentakills(
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

            if (!byKiller.TryGetValue(death.KillerHero, out List<TeamKillDeath> list))
            {
                list = new List<TeamKillDeath>();
                byKiller[death.KillerHero] = list;
            }

            list.Add(death);
        }

        var clips = new List<TeamKillClip>();
        foreach ((string hero, List<TeamKillDeath> victims) in byKiller)
        {
            victims.Sort((left, right) => left.Second.CompareTo(right.Second));
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
                    clips.Add(
                        Clip(
                            PentakillKind,
                            hero,
                            victims[offset].Second,
                            victims[offset + streak.Kills - 1].Second,
                            leadSeconds,
                            tailSeconds,
                            DescribePentakill(hero, victims, offset, streak.Kills)
                        )
                    );
                }

                offset += streak.Kills;
            }
        }

        return clips;
    }

    private static string DescribePentakill(
        string hero,
        List<TeamKillDeath> victims,
        int offset,
        int kills
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < kills; i++)
        {
            seen.Add(victims[offset + i].VictimHero);
        }

        return seen.Count >= 5 ? hero + " pentakill (team wipe)" : hero + " pentakill";
    }

    private static List<TeamKillClip> TeamWipes(
        IReadOnlyList<TeamKillDeath> deaths,
        int windowSeconds,
        int leadSeconds,
        int tailSeconds
    )
    {
        var ordered = new List<TeamKillDeath>(deaths.Count);
        foreach (TeamKillDeath death in deaths)
        {
            if (!string.IsNullOrWhiteSpace(death.VictimHero))
            {
                ordered.Add(death);
            }
        }

        ordered.Sort((left, right) => left.Second.CompareTo(right.Second));
        var clips = new List<TeamKillClip>();
        int start = 0;
        while (start < ordered.Count)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            int end = -1;
            for (int index = start; index < ordered.Count; index++)
            {
                if (ordered[index].Second - ordered[start].Second > windowSeconds)
                {
                    break;
                }

                unique.Add(ordered[index].VictimHero);
                if (unique.Count >= 5)
                {
                    end = index;
                    break;
                }
            }

            if (end < 0)
            {
                start++;
                continue;
            }

            clips.Add(
                Clip(
                    TeamWipeKind,
                    PrimaryKiller(ordered, start, end),
                    ordered[start].Second,
                    ordered[end].Second,
                    leadSeconds,
                    tailSeconds,
                    "team wipe"
                )
            );
            start = end + 1;
        }

        return clips;
    }

    private static string PrimaryKiller(List<TeamKillDeath> deaths, int start, int end)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = start; index <= end; index++)
        {
            string killer = deaths[index].KillerHero;
            if (string.IsNullOrWhiteSpace(killer))
            {
                continue;
            }

            counts.TryGetValue(killer, out int count);
            counts[killer] = count + 1;
        }

        string best = string.Empty;
        int bestCount = 0;
        foreach ((string hero, int count) in counts)
        {
            if (count > bestCount || (count == bestCount && string.CompareOrdinal(hero, best) < 0))
            {
                best = hero;
                bestCount = count;
            }
        }

        return best;
    }

    private static TeamKillClip Clip(
        string kind,
        string hero,
        int firstDeath,
        int lastDeath,
        int leadSeconds,
        int tailSeconds,
        string description
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
            description
        );
    }
}
