using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Awards;

/// <summary>
/// The game's own end-of-match awards that stand out (#512): the award, and a score-screen count
/// past its threshold. Positive call-outs only. Strongest first within a kind, kinds in enum order.
/// </summary>
public sealed record AwardInsights
{
    public IReadOnlyList<PlayerAward> Awards { get; set; } = Array.Empty<PlayerAward>();
}

/// <summary>An award with the count behind it. Hero in English.</summary>
public sealed record PlayerAward
{
    public int PlayerIndex { get; init; }
    public int Team { get; init; }
    public string Hero { get; init; }
    public AwardKind Kind { get; init; }
    public int Value { get; init; }
}

/// <summary>
/// The awards a title may name. Most Daredevil Escapes is not one: in 1,975 Storm League games the
/// game gave it once, to a hero with 3 escapes and a 1/3 score line.
/// </summary>
public enum AwardKind
{
    /// <summary><c>HatTrick</c>, rare enough on its own (once in 1,975 Storm League games); the value is the hero's kills.</summary>
    HatTrick,

    /// <summary><c>ClutchHealer</c>, counted by clutch heals (a heal on an ally about to die).</summary>
    ClutchHealer,

    /// <summary><c>MostVengeancesPerformed</c>, counted by vengeances (killing an enemy who just killed an ally).</summary>
    Avenger,

    /// <summary><c>MostEscapes</c>, counted by escapes (surviving at low health).</summary>
    EscapeArtist,
}
