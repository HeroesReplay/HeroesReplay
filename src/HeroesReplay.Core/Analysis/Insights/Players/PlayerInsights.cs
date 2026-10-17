using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Players;

/// <summary>Every player's score line, the game's MVP, and the positive standouts (#412).</summary>
public sealed record PlayerInsights
{
    public IReadOnlyList<PlayerLine> Lines { get; set; } = Array.Empty<PlayerLine>();

    /// <summary>The player with the game's MVP award, or null. Either team can have it.</summary>
    public PlayerLine Mvp { get; init; }

    /// <summary>Standout performances, most remarkable first.</summary>
    public IReadOnlyList<Standout> Standouts { get; set; } = Array.Empty<Standout>();
}

/// <summary>One player's score line, hero in English.</summary>
public sealed record PlayerLine
{
    public int PlayerIndex { get; init; }
    public int Team { get; init; }
    public string Hero { get; init; }
    public bool IsAi { get; init; }
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public int Takedowns { get; init; }
    public int HeroDamage { get; init; }

    /// <summary>The player's share of their team's hero damage, 0 to 1.</summary>
    public double HeroDamageShare { get; init; }

    public bool Mvp { get; init; }

    /// <summary>Kills/deaths/assists as the score screen writes them: <c>12/0/9</c>.</summary>
    public string Kda => $"{Kills}/{Deaths}/{Assists}";
}

/// <summary>A positive standout. There is no kind for a struggling player (#412 decision).</summary>
public enum StandoutKind
{
    /// <summary>No deaths and many kills.</summary>
    DeathlessCarry,

    /// <summary>Many kills and at most one death.</summary>
    KillLeader,

    /// <summary>Many kills, at least twice every teammate's.</summary>
    TeamKillLeader,
}

public sealed record Standout(StandoutKind Kind, PlayerLine Player);
