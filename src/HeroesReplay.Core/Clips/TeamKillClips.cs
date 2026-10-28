using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    IReadOnlyList<TeamKillBlow> Kills = null,
    bool NamesTeam = false
)
{
    /// <summary>
    /// <see cref="Hero"/> names a team (<c>Blue team</c>), not a hero: a boss stolen from the team
    /// that killed it (#528). The clip's entry then names no hero.
    /// </summary>
    public bool NamesTeam { get; init; } = NamesTeam;

    /// <summary>
    /// The pentakill's blows killed five different enemy heroes: the whole team. It is a fact
    /// about this one clip, which the media score and the full match tags use. It is never a
    /// clip of its own (#369).
    /// </summary>
    public bool WipedTeam => TeamKillClips.UniqueVictims(Kills) >= 5;
}

/// <summary>
/// One player's own feats, so clips stay rare. A wipe shared across players is never a clip.
/// <list type="bullet">
/// <item><b>Pentakill:</b> five or more hero-unit killing blows, each within the window of the
/// previous one. A pentakill that killed the whole team is still one clip
/// (<see cref="TeamKillClip.WipedTeam"/>, #369). Only a pentakill makes a replay Notable
/// (<see cref="MediaPolicy.ReplayMediaEvidence.Accepted"/>).</item>
/// <item><b>Triple kill (#511):</b> a burst of three or more blows within
/// <see cref="KillBursts.SpanSeconds"/> of the first (<see cref="KillBursts"/>), that is not
/// part of a pentakill. It is cut and uploaded as a clip but never makes the replay Notable (owner
/// decision 2026-10-25). At most <see cref="MaxTripleKills"/> a replay.</item>
/// </list>
/// </summary>
public static class TeamKillClips
{
    public const string PentakillKind = "pentakill";

    public const string TripleKillKind = "triple-kill";

    /// <summary>The most triple-kill clips one replay is cut into, the earliest first.</summary>
    public const int MaxTripleKills = 2;

    /// <summary>The word a clip's title and tags use for its kind (<c>triple kill</c>, <c>quadra kill</c>, <c>boss steal</c>).</summary>
    public static string Title(TeamKillClip clip) =>
        clip.Kind switch
        {
            TripleKillKind => clip.Kills?.Count >= 4 ? "quadra kill" : "triple kill",
            BossFightClips.StealKind => "boss steal",
            BossFightClips.DefendedKind => "boss defended",
            _ => clip.Kind,
        };

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
        List<TeamKillClip> tripleKills = clips
            .Where(clip => clip.Kind == TripleKillKind)
            .OrderBy(clip => clip.FirstDeathSecond)
            .ToList();
        foreach (TeamKillClip extra in tripleKills.Skip(MaxTripleKills))
        {
            clips.Remove(extra);
        }

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
            var pentakills = new List<KillStreak>();
            int offset = 0;
            foreach (KillStreak streak in streaks)
            {
                if (streak.Kills >= 5)
                {
                    pentakills.Add(streak);
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

            // A burst inside a pentakill is part of that clip, not one of its own.
            foreach (KillBurst burst in KillBursts.Find(seconds))
            {
                if (
                    pentakills.Any(streak =>
                        burst.StartSecond <= streak.EndSecond
                        && burst.EndSecond >= streak.StartSecond
                    )
                )
                {
                    continue;
                }

                TeamKillBlow[] blows = Blows(victims, burst.Start, burst.Kills);
                clips.Add(
                    Clip(
                        TripleKillKind,
                        hero,
                        burst.StartSecond,
                        burst.EndSecond,
                        leadSeconds,
                        tailSeconds,
                        hero + (burst.Kills >= 4 ? " quadra kill" : " triple kill"),
                        blows
                    )
                );
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
