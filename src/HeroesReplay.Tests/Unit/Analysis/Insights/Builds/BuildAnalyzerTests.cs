using HeroesReplay.Core.Analysis.Insights.Builds;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Builds.Matching;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Tests.Unit.Builds;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Builds;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BuildAnalyzerTests
{
    [Fact]
    public void EachHumanPlayerGetsTheirBestGuideBuild()
    {
        var guides = new GuideCatalog(
            new GuideReference
            {
                Heroes = new[]
                {
                    BuildFixtures.Guide(
                        BuildFixtures.Build(
                            "Teleport Build",
                            GuideTags.Recommended,
                            "ynn",
                            "ynn",
                            "ynn",
                            "yn",
                            "ynn",
                            "ynn",
                            "ynnn"
                        ),
                        BuildFixtures.Build(
                            "Artillery Build",
                            GuideTags.Beginner,
                            "nyn",
                            "nyn",
                            "nyn",
                            "ny",
                            "nyn",
                            "nyn",
                            "nynn"
                        )
                    ),
                },
            }
        );
        SnapshotPlayer liMing = new()
        {
            Index = 3,
            Team = 0,
            Hero = BuildFixtures.Hero(),
            Talents = new[] { "T01", "T11", "T21", "T32", "T41" },
        };
        SnapshotPlayer ai = liMing with { Index = 4, IsAi = true };
        ReplaySnapshot snapshot = InsightFixtures.Snapshot(new[] { liMing, ai });

        BuildInsights builds = new BuildAnalyzer(guides, new BuildMatcher()).Analyze(snapshot);

        PlayerBuild build = Assert.Single(builds.Players);
        Assert.Equal(
            (3, "Li-Ming", "Teleport Build"),
            (build.PlayerIndex, build.Hero, build.Build)
        );
        Assert.Equal(0.8, build.Score, 3);
        Assert.Equal((4, 5), (build.Recommended, build.Tiers));
        Assert.Equal("Artillery Build", build.RunnerUp);
        Assert.Equal("https://www.icy-veins.com/heroes/li-ming-build-guide", build.GuideUrl);
    }
}
