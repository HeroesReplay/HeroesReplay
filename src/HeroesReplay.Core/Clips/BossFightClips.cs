using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Clips;

/// <summary>
/// Boss fights worth a clip (#528), from the <see cref="BossFightAnalyzer"/>: a
/// <see cref="BossFightKind.Steal"/> and a <see cref="BossFightKind.Defended"/> boss. Each clip is
/// named after the hero whose unit killed the boss, or after the capturing team when the other
/// team killed it (<see cref="TeamKillClip.NamesTeam"/>). Like a triple kill, a boss clip is cut and
/// uploaded but never makes the replay Notable. At most <see cref="MaxClips"/> a replay, steals
/// first, and none that overlaps a pentakill or triple-kill clip: that moment is already cut.
/// </summary>
public static class BossFightClips
{
    public const string StealKind = "boss-steal";

    public const string DefendedKind = "boss-defended";

    /// <summary>The most boss clips one replay is cut into.</summary>
    public const int MaxClips = 2;

    /// <summary>A clip shows at most this much of the fight before the capture.</summary>
    public const int MaxFightSeconds = 35;

    public const int LeadSeconds = 3;

    public const int TailSeconds = 6;

    public static IReadOnlyList<TeamKillClip> Select(
        BossFightInsights insights,
        IReadOnlyList<TeamKillClip> killClips = null
    )
    {
        if (insights?.Fights == null || insights.Fights.Count == 0)
        {
            return Array.Empty<TeamKillClip>();
        }

        var clips = new List<TeamKillClip>();
        foreach (
            BossFight fight in insights
                .Fights.Where(fight => fight.Kind is BossFightKind.Steal or BossFightKind.Defended)
                .OrderBy(fight => fight.Kind == BossFightKind.Steal ? 0 : 1)
                .ThenBy(fight => fight.Second)
        )
        {
            TeamKillClip clip = Clip(fight);
            if (
                (killClips ?? Array.Empty<TeamKillClip>()).Any(kill => Overlaps(kill, clip))
                || clips.Any(other => Overlaps(other, clip))
            )
            {
                continue;
            }

            clips.Add(clip);
            if (clips.Count == MaxClips)
            {
                break;
            }
        }

        clips.Sort((left, right) => left.HudStartSecond.CompareTo(right.HudStartSecond));
        return clips;
    }

    private static TeamKillClip Clip(BossFight fight)
    {
        bool steal = fight.Kind == BossFightKind.Steal;
        string team = ReplaySnapshot.TeamName(fight.Team);
        bool namesTeam = fight.KilledByOtherTeam || string.IsNullOrWhiteSpace(fight.KillerHero);
        string hero = namesTeam ? team + " team" : fight.KillerHero;
        int first = Math.Max(fight.StartSecond, fight.Second - MaxFightSeconds);
        string description;
        if (fight.KilledByOtherTeam)
        {
            string killers = ReplaySnapshot.TeamName(fight.KillerTeam.Value);
            string blow = string.IsNullOrWhiteSpace(fight.KillerHero)
                ? string.Empty
                : $" ({fight.KillerHero} landed the killing blow)";
            description = steal
                ? $"{team} steals the boss {killers} killed{blow}"
                : $"{team} keeps the boss {killers} tried to steal{blow}";
        }
        else if (steal)
        {
            BossFightTeam taking = fight.Teams[fight.Starter.Value];
            description =
                $"{hero} steals the boss for {team}: {ReplaySnapshot.TeamName(taking.Team)} had up to {taking.MostHeroes} heroes on it for {taking.GroupSeconds} s";
        }
        else
        {
            int deaths = fight.Teams.Sum(entry => entry.Deaths);
            description =
                $"{hero} secures the boss for {team} through a {fight.TogetherSeconds} s fight at the pit, {deaths} heroes down";
        }

        return new TeamKillClip(
            steal ? StealKind : DefendedKind,
            hero,
            first,
            fight.Second,
            Math.Max(0, first - LeadSeconds),
            fight.Second + TailSeconds,
            description,
            NamesTeam: namesTeam
        );
    }

    private static bool Overlaps(TeamKillClip left, TeamKillClip right) =>
        left.HudStartSecond <= right.HudEndSecond && right.HudStartSecond <= left.HudEndSecond;
}
