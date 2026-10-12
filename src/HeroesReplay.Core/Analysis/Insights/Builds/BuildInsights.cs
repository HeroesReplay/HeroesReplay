using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Builds;

/// <summary>The guide build each player's talents fit best (#412).</summary>
public sealed record BuildInsights
{
    public IReadOnlyList<PlayerBuild> Players { get; init; } = Array.Empty<PlayerBuild>();
}

/// <summary>One player's best guide build: names and links only, never guide text.</summary>
public sealed record PlayerBuild
{
    public int PlayerIndex { get; init; }

    public int Team { get; init; }

    public string Hero { get; init; }

    /// <summary>The build's name as the guide writes it (<c>Teleport Build</c>).</summary>
    public string Build { get; init; }

    public string Tag { get; init; }

    /// <summary>0 to 1: 1 when every pick is one the build recommends.</summary>
    public double Score { get; init; }

    /// <summary>Picks the build recommends.</summary>
    public int Recommended { get; init; }

    /// <summary>Picks the build lists as situational.</summary>
    public int Situational { get; init; }

    /// <summary>Picks compared.</summary>
    public int Tiers { get; init; }

    /// <summary>The runner-up scored within the ambiguity margin.</summary>
    public bool Ambiguous { get; init; }

    public string RunnerUp { get; init; }

    public string GuideUrl { get; init; }

    public string CalculatorUrl { get; init; }
}
