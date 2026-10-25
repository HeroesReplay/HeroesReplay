using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Bursts;

/// <summary>
/// Each player's kill bursts (#512): hero killing blows in quick succession, the triple kills the
/// clips cut (#511). Players with the most bursts come first.
/// </summary>
public sealed record BurstInsights
{
    public IReadOnlyList<PlayerBursts> Players { get; set; } = Array.Empty<PlayerBursts>();
}

/// <summary>One player's bursts, earliest first. Hero in English.</summary>
public sealed record PlayerBursts
{
    public int PlayerIndex { get; init; }
    public int Team { get; init; }
    public string Hero { get; init; }
    public IReadOnlyList<Burst> Bursts { get; set; } = Array.Empty<Burst>();
}

/// <summary>A burst: <see cref="Kills"/> blows, the first at <see cref="Second"/> on the in-game clock (the time base clips use).</summary>
public sealed record Burst(int Second, int Kills);
