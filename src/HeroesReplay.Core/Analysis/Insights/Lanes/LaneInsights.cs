using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Lanes;

/// <summary>Who soaked a side lane while the laning lasted, and each team's solo laner (#412).</summary>
public sealed record LaneInsights
{
    public TimeSpan WindowStart { get; init; }

    public TimeSpan WindowEnd { get; init; }

    /// <summary>2 on Battlefield of Eternity, Braxis Holdout, Hanamura and Haunted Mines; 3 elsewhere.</summary>
    public int LaneCount { get; init; }

    public IReadOnlyList<TeamLanes> Teams { get; init; } = Array.Empty<TeamLanes>();
}

/// <summary>One team's lanes in the laning window.</summary>
public sealed record TeamLanes
{
    public int Team { get; init; }

    /// <summary>The lane the team's grouped players spent the window in.</summary>
    public int GroupLane { get; init; }

    /// <summary>Players mostly alone in a side lane, most alone first.</summary>
    public IReadOnlyList<LaneSoaker> Soakers { get; init; } = Array.Empty<LaneSoaker>();

    /// <summary>The solo laner, or null when <see cref="Confidence"/> is <see cref="LaneConfidence.None"/>.</summary>
    public LaneSoaker SoloLaner { get; init; }

    public LaneConfidence Confidence { get; init; }
}

/// <summary>A player who soaked a side lane.</summary>
public sealed record LaneSoaker
{
    public int PlayerIndex { get; init; }

    public string Hero { get; init; }

    public int Lane { get; init; }

    /// <summary>The share of the player's samples in <see cref="Lane"/>.</summary>
    public double LaneShare { get; init; }

    /// <summary>The share of the player's samples with no teammate near.</summary>
    public double AloneShare { get; init; }

    /// <summary>The other team also had a soaker in this lane: the two laned against each other.</summary>
    public bool Mirrored { get; init; }

    /// <summary>The player was in the team's top two for minion damage (true for 81% of solo laners measured).</summary>
    public bool TopMinionDamage { get; init; }
}

/// <summary>How sure the solo laner is. Titles and descriptions name only <see cref="High"/> (#412 decision).</summary>
public enum LaneConfidence
{
    None,

    /// <summary>Several soakers, or one that is neither mirrored nor a top minion damage dealer.</summary>
    Low,

    /// <summary>One soaker, not mirrored, but in the team's top two for minion damage.</summary>
    Medium,

    /// <summary>One soaker, and the other team's soaker in the same lane.</summary>
    High,
}
