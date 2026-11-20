using HeroesReplay.Core.Analysis.Insights.Bosses;
using HeroesReplay.Core.Analysis.Insights.BSteps;

namespace HeroesReplay.Core.Clips;

/// <summary>
/// What a clip's title may say about it beyond its kind and hero (<c>ClipTitle</c>): the bstep or
/// the boss fight it was cut from, and the team levels at a boss capture. Each is null when the
/// clip is not of that kind or the replay did not give it. A kill clip needs none: its blows
/// (<see cref="TeamKillClip.Kills"/>) carry its facts.
/// </summary>
public sealed record ClipFacts
{
    public BStep BStep { get; init; }

    public BossFight Boss { get; init; }

    /// <summary>The capturing team's level at the capture, or null when the replay has no levels.</summary>
    public int? CapturerLevel { get; init; }

    /// <summary>The other team's level at the capture, or null when the replay has no levels.</summary>
    public int? OtherLevel { get; init; }

    /// <summary>How many levels the capturing team was behind (negative when ahead), or null.</summary>
    public int? LevelsBehind =>
        CapturerLevel is int mine && OtherLevel is int theirs ? theirs - mine : null;
}
