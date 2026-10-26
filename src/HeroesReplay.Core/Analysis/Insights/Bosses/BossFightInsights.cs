using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Bosses;

/// <summary>
/// Each boss capture and the fight at the pit before it (#528): who was there, who got there
/// first, who died there, and who took it. Earliest first.
/// </summary>
public sealed record BossFightInsights
{
    public IReadOnlyList<BossFight> Fights { get; set; } = Array.Empty<BossFight>();
}

/// <summary>What the fight at the pit was, read from where the heroes were.</summary>
public enum BossFightKind
{
    /// <summary>An ordinary take: nothing about it stands out.</summary>
    Take,

    /// <summary>
    /// The team that captured the boss is not the team that was taking it: the other team landed
    /// the boss's killing blow (the replay records both) and the capturing team had not clearly
    /// started it, or a group of the other team's heroes was at the pit well before the capturing
    /// team arrived.
    /// </summary>
    Steal,

    /// <summary>
    /// The team that clearly started the boss captured it anyway: through a fight where both teams'
    /// groups were at the pit together and heroes died there, or after the other team landed the
    /// killing blow (a steal that failed at the circle).
    /// </summary>
    Defended,
}

/// <summary>
/// One boss capture. Seconds are the in-game clock, the time base clips use. <see cref="Starter"/>
/// is the team whose group reached the pit first, or null when no team grouped there.
/// </summary>
public sealed record BossFight
{
    public int CampId { get; init; }

    /// <summary>The team that captured the boss.</summary>
    public int Team { get; init; }

    /// <summary>When the boss was captured.</summary>
    public int Second { get; init; }

    /// <summary>When the first hero reached the pit, the fight's start.</summary>
    public int StartSecond { get; init; }

    public int? Starter { get; init; }

    /// <summary>The team whose half of the map the pit is in, or -1 in the middle.</summary>
    public int Side { get; set; } = -1;

    /// <summary>The player whose unit killed the boss, or -1.</summary>
    public int KillerIndex { get; set; } = -1;

    /// <summary>That player's hero in English, or null.</summary>
    public string KillerHero { get; init; }

    /// <summary>That player's team, or null. The other team's means the capture took the boss from it.</summary>
    public int? KillerTeam { get; init; }

    /// <summary>The team that killed the boss did not capture it.</summary>
    public bool KilledByOtherTeam => KillerTeam is int team && team != Team;

    /// <summary>Blue (0), then Red (1).</summary>
    public IReadOnlyList<BossFightTeam> Teams { get; set; } = Array.Empty<BossFightTeam>();

    /// <summary>Seconds in which both teams had a group at the pit.</summary>
    public int TogetherSeconds { get; init; }

    public BossFightKind Kind { get; init; }
}

/// <summary>One team at the pit during the fight.</summary>
public sealed record BossFightTeam
{
    public int Team { get; init; }

    /// <summary>Each second, the team's heroes at the pit, added up.</summary>
    public int HeroSeconds { get; init; }

    /// <summary>Seconds in which the team had a group at the pit.</summary>
    public int GroupSeconds { get; init; }

    /// <summary>The most of the team's heroes at the pit at once.</summary>
    public int MostHeroes { get; init; }

    /// <summary>When the team's first hero reached the pit, or null.</summary>
    public int? FirstHeroSecond { get; init; }

    /// <summary>When the team first had a group at the pit, or null.</summary>
    public int? FirstGroupSecond { get; init; }

    /// <summary>The team's heroes who died at the pit during the fight.</summary>
    public int Deaths { get; init; }
}
