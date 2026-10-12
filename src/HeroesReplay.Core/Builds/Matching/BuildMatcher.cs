using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Builds.Played;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Builds.Matching;

/// <summary>
/// Finds the guide build a player's talents fit best (#412). In ARAM only the ARAM builds are
/// candidates, elsewhere every other build; a guide with no candidate of that kind uses all of
/// them. A tie goes to the guide's preferred tag (<see cref="GuideTags.Rank"/>), then page order.
/// </summary>
public sealed class BuildMatcher
{
    private readonly BuildMatchOptions options;

    public BuildMatcher(BuildMatchOptions options = null)
    {
        this.options = options ?? new BuildMatchOptions();
    }

    /// <summary>
    /// The best build for <paramref name="played"/>, or null when there is no guide, no build, or
    /// the player picked fewer than <see cref="BuildMatchOptions.MinTiers"/> tiers.
    /// </summary>
    public BuildMatch Match(PlayedBuild played, HeroGuide guide, bool aram)
    {
        if (played == null || guide?.Builds == null || played.Picks.Count < options.MinTiers)
        {
            return null;
        }

        List<GuideBuild> candidates = guide
            .Builds.Where(build => build != null && GuideTags.IsAram(build.Tag) == aram)
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = guide.Builds.Where(build => build != null).ToList();
        }

        List<BuildScore> ranked = candidates
            .Select((build, order) => (Score: Score(played, build), Order: order))
            .Where(scored => scored.Score.Tiers >= options.MinTiers)
            .OrderByDescending(scored => scored.Score.Score)
            .ThenBy(scored => GuideTags.Rank(scored.Score.Build.Tag))
            .ThenBy(scored => scored.Order)
            .Select(scored => scored.Score)
            .ToList();
        if (ranked.Count == 0)
        {
            return null;
        }

        BuildScore best = ranked[0];
        BuildScore runnerUp = ranked.Count > 1 ? ranked[1] : null;
        bool ambiguous = runnerUp != null && best.Score - runnerUp.Score < options.AmbiguityMargin;
        return new BuildMatch(guide, played, best, runnerUp, ambiguous);
    }

    public BuildScore Score(PlayedBuild played, GuideBuild build)
    {
        if (played == null || build?.Tiers == null)
        {
            return new BuildScore(build, 0, 0, 0);
        }

        double sum = 0;
        int tiers = 0;
        int recommended = 0;
        int situational = 0;
        foreach (HeroTalent pick in played.Picks)
        {
            if (pick.Tier < 0 || pick.Tier >= build.Tiers.Count)
            {
                continue;
            }

            GuideTier tier = build.Tiers[pick.Tier];
            if (tier == null || tier.Stale)
            {
                continue;
            }

            tiers++;
            if (Contains(tier.Recommended, pick))
            {
                sum += 1;
                recommended++;
            }
            else if (Contains(tier.Situational, pick))
            {
                sum += options.SituationalWeight;
                situational++;
            }
        }

        return new BuildScore(build, tiers == 0 ? 0 : sum / tiers, tiers, recommended, situational);
    }

    private static bool Contains(IReadOnlyList<string> talentIds, HeroTalent pick) =>
        talentIds != null && talentIds.Contains(pick.TalentId, StringComparer.Ordinal);
}
