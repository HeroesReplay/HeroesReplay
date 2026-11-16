using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.BSteps;

/// <summary>Every bstep in the replay (<see cref="BStepAnalyzer"/>), earliest first.</summary>
public sealed record BStepInsights
{
    public IReadOnlyList<BStep> BSteps { get; set; } = Array.Empty<BStep>();
}

/// <summary>
/// One player bstepping: <see cref="Cycles"/> Hearthstone starts, each cancelled by a move, from
/// <see cref="StartSecond"/> to <see cref="EndSecond"/> (replay time, the time base clips use).
/// <see cref="Deaths"/> are the hero deaths around it that involve the bstepper or happened next to
/// them; a bstep with one is the drama a clip shows.
/// </summary>
public sealed record BStep
{
    public int PlayerIndex { get; init; }

    public int Team { get; init; }

    /// <summary>The bstepping hero in English, or null when the catalog has no match.</summary>
    public string Hero { get; init; }

    public int StartSecond { get; init; }

    public int EndSecond { get; init; }

    public int Cycles { get; init; }

    /// <summary>The middle gap between two Hearthstone starts, in seconds.</summary>
    public double MedianGapSeconds { get; init; }

    public IReadOnlyList<BStepDeath> Deaths { get; init; } = Array.Empty<BStepDeath>();
}

/// <summary>How a death relates to the bstepper.</summary>
public enum BStepDeathKind
{
    /// <summary>The bstepper died.</summary>
    Bstepper,

    /// <summary>The bstepper was credited with the kill.</summary>
    KilledByBstepper,

    /// <summary>An enemy of the bstepper died next to them.</summary>
    Enemy,

    /// <summary>An ally of the bstepper died next to them.</summary>
    Ally,
}

/// <summary>
/// A hero death linked to a bstep: <see cref="Second"/> is replay time, and
/// <see cref="After"/> says it came after the bstep ended rather than before it started (or during it).
/// </summary>
public sealed record BStepDeath(
    int PlayerIndex,
    string Hero,
    int Second,
    BStepDeathKind Kind,
    bool After,
    int KillerIndex = -1
);
