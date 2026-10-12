namespace HeroesReplay.Core.Builds.Matching;

/// <summary>How a player's talents are compared with a guide's builds (#412).</summary>
public sealed class BuildMatchOptions
{
    /// <summary>A player needs this many picked tiers before any build is matched.</summary>
    public int MinTiers { get; init; } = 4;

    /// <summary>What a pick the build calls situational is worth. A recommended pick is worth 1.</summary>
    public double SituationalWeight { get; init; } = 0.5;

    /// <summary>The best build is ambiguous when the runner-up scores within this much of it.</summary>
    public double AmbiguityMargin { get; init; } = 0.10;
}
