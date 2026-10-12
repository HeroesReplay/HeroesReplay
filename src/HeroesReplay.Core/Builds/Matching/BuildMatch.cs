using HeroesReplay.Core.Builds.Played;
using HeroesReplay.Core.Builds.Reference;

namespace HeroesReplay.Core.Builds.Matching;

/// <summary>
/// How well a player's talents fit one build: the average over the tiers the player picked and
/// the build scores (1 for a recommended pick, the situational weight for a situational one).
/// </summary>
/// <param name="Build">The guide build.</param>
/// <param name="Score">0 to 1.</param>
/// <param name="Tiers">Tiers compared: the player's picks in tiers the build does not mark stale.</param>
/// <param name="Recommended">How many of those picks the build recommends.</param>
public sealed record BuildScore(GuideBuild Build, double Score, int Tiers, int Recommended);

/// <summary>The build a player's talents fit best, and the runner-up.</summary>
/// <param name="Guide">The hero's guide.</param>
/// <param name="Played">The player's picks.</param>
/// <param name="Best">The best build.</param>
/// <param name="RunnerUp">The next best build, or null when the guide has one candidate.</param>
/// <param name="Ambiguous">True when the runner-up is within the ambiguity margin of the best.</param>
public sealed record BuildMatch(
    HeroGuide Guide,
    PlayedBuild Played,
    BuildScore Best,
    BuildScore RunnerUp,
    bool Ambiguous
);
