namespace HeroesReplay.Core.Analysis.Insights.Objectives;

/// <summary>
/// The player who did most for the map objective (#512), from the score screen's map value
/// (Immortal damage, gems, time in the temple) with the game's own objective award as a
/// cross-check. Null on a map without a value.
/// </summary>
public sealed record ObjectiveInsights
{
    /// <summary>What the map's value measures, in title wording: <c>Immortal damage</c>. Null when the map has none.</summary>
    public string Label { get; init; }

    /// <summary>The player with the most of it, or null.</summary>
    public ObjectiveLeader Leader { get; init; }

    /// <summary>The leader's share and lead clear the thresholds and the game gave them the award.</summary>
    public bool Standout { get; init; }
}

/// <summary>The objective's leader. <see cref="Share"/> is of every player's total, 0 to 1.</summary>
public sealed record ObjectiveLeader
{
    public int PlayerIndex { get; init; }
    public int Team { get; init; }
    public string Hero { get; init; }
    public int Value { get; init; }
    public double Share { get; init; }

    /// <summary>The next player's value.</summary>
    public int Next { get; init; }

    /// <summary>The game gave the leader the map's objective award (<c>MostImmortalDamage</c>).</summary>
    public bool Award { get; init; }
}
