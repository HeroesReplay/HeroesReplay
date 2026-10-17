using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Camps;

/// <summary>Merc camp control per team (#412). ARAM has no camps, so it is empty there.</summary>
public sealed record CampInsights
{
    public IReadOnlyList<TeamCamps> Teams { get; set; } = Array.Empty<TeamCamps>();

    public int TotalCaptures { get; init; }
}

/// <summary>One team's captures.</summary>
public sealed record TeamCamps
{
    public int Team { get; init; }

    public int Captures { get; init; }

    /// <summary>Captures of camps on the other team's side of the map (nearer the other core).</summary>
    public int EnemySideCaptures { get; init; }

    public int BossCaptures { get; init; }

    /// <summary>The team took almost no camps in a full-length game (9 per 100 games measured).</summary>
    public bool Drought { get; init; }
}
