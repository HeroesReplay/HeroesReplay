using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Builds.Matching;
using HeroesReplay.Core.Builds.Played;
using HeroesReplay.Core.Builds.Reference;

namespace HeroesReplay.Core.Analysis.Insights.Builds;

/// <summary>
/// Matches every player's talents to their hero's guide builds (#412). Measured on 1,070
/// players: 61% fit a build at 0.70 or more, 31% at 0.85 or more, 11% are off-guide.
/// </summary>
public sealed class BuildAnalyzer : IInsightAnalyzer<BuildInsights>
{
    private readonly IGuideCatalog guides;
    private readonly BuildMatcher matcher;

    public BuildAnalyzer(IGuideCatalog guides, BuildMatcher matcher)
    {
        this.guides = guides ?? throw new ArgumentNullException(nameof(guides));
        this.matcher = matcher ?? new BuildMatcher();
    }

    public BuildInsights Analyze(ReplaySnapshot snapshot)
    {
        var players = new List<PlayerBuild>();
        foreach (SnapshotPlayer player in snapshot?.Players ?? Array.Empty<SnapshotPlayer>())
        {
            if (player.Hero == null || player.IsAi)
            {
                continue;
            }

            BuildMatch match = matcher.Match(
                PlayedBuilds.Read(player.Hero, player.Talents),
                guides.Find(player.Hero),
                snapshot.Aram
            );
            if (match == null)
            {
                continue;
            }

            players.Add(
                new PlayerBuild
                {
                    PlayerIndex = player.Index,
                    Team = player.Team,
                    Hero = player.HeroName,
                    Build = match.Best.Build.Name,
                    Tag = match.Best.Build.Tag,
                    Score = Math.Round(match.Best.Score, 3),
                    Recommended = match.Best.Recommended,
                    Situational = match.Best.Situational,
                    Tiers = match.Best.Tiers,
                    Ambiguous = match.Ambiguous,
                    RunnerUp = match.RunnerUp?.Build.Name,
                    GuideUrl = match.Guide.GuideUrl,
                    CalculatorUrl = match.Best.Build.CalculatorUrl,
                }
            );
        }

        return new BuildInsights
        {
            Players = players
                .OrderBy(player => player.Team)
                .ThenBy(player => player.PlayerIndex)
                .ToList(),
        };
    }
}
