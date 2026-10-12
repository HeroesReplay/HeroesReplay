namespace HeroesReplay.Core.Analysis.Insights.Comeback;

/// <summary>
/// A comeback (#412): the team whose core stood at the end had lost more buildings, or trailed by
/// levels, before the other core fell. Titles call it an insane comeback and name no team.
/// </summary>
public sealed record ComebackInsights
{
    public bool Comeback { get; init; }

    /// <summary>Towers, forts and keeps the surviving team lost, minus those the other team lost.</summary>
    public int BuildingDeficit { get; init; }

    /// <summary>The most levels the surviving team was behind at any point.</summary>
    public int MaxLevelDeficit { get; init; }
}
