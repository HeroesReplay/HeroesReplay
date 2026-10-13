using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Structures;

/// <summary>Forts and keeps each team lost (#412).</summary>
public sealed record StructureInsights
{
    public IReadOnlyList<TeamStructures> Teams { get; init; } = Array.Empty<TeamStructures>();
}

public sealed record TeamStructures
{
    public int Team { get; init; }

    public int FortsLost { get; init; }

    public int KeepsLost { get; init; }

    /// <summary>When the team lost its last fort or keep, or null when it lost none.</summary>
    public TimeSpan? LastLoss { get; init; }
}
