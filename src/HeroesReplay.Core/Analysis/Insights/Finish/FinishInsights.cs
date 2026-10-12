namespace HeroesReplay.Core.Analysis.Insights.Finish;

/// <summary>
/// How the match ended (#412), without saying who won: a core race is a finish where both teams
/// had lost keeps and the team whose core fell still had heroes at the other core.
/// </summary>
public sealed record FinishInsights
{
    public bool CoreRace { get; init; }

    /// <summary>Heroes of the team whose core fell that were at the other core in the final window.</summary>
    public int AttackersAtOtherCore { get; init; }

    /// <summary>The fewer keeps either team lost.</summary>
    public int KeepsLostEach { get; init; }
}
