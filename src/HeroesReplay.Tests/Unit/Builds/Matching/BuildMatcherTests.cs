using HeroesReplay.Core.Builds.Matching;
using HeroesReplay.Core.Builds.Played;
using HeroesReplay.Core.Builds.Reference;
using Xunit;

namespace HeroesReplay.Tests.Unit.Builds.Matching;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BuildMatcherTests
{
    private static readonly string[] AllFirst = { "ynn", "ynn", "ynn", "yn", "ynn", "ynn", "ynnn" };

    [Fact]
    public void Score_IsOneForARecommendedPickAndHalfForASituationalOne()
    {
        GuideBuild build = BuildFixtures.Build(
            "Teleport Build",
            GuideTags.Recommended,
            "ysn",
            "ysn",
            "ysn",
            "ys",
            "ysn",
            "ysn",
            "ysnn"
        );
        PlayedBuild played = Played("T01", "T12", "T23", "T31");

        BuildScore score = new BuildMatcher().Score(played, build);

        // T01 recommended (1), T12 situational (0.5), T23 neither (0), T31 recommended (1).
        Assert.Equal(4, score.Tiers);
        Assert.Equal(2, score.Recommended);
        Assert.Equal(2.5 / 4, score.Score, 3);
    }

    [Fact]
    public void Match_TakesTheHighestScore()
    {
        HeroGuide guide = BuildFixtures.Guide(
            BuildFixtures.Build("Teleport Build", GuideTags.Recommended, AllFirst),
            BuildFixtures.Build(
                "Artillery Build",
                GuideTags.Situational,
                "nyn",
                "nyn",
                "nyn",
                "ny",
                "nyn",
                "nyn",
                "nynn"
            )
        );

        BuildMatch match = new BuildMatcher().Match(
            Played("T02", "T12", "T22", "T32", "T42"),
            guide,
            aram: false
        );

        Assert.Equal("Artillery Build", match.Best.Build.Name);
        Assert.Equal(1, match.Best.Score, 3);
        Assert.Equal("Teleport Build", match.RunnerUp.Build.Name);
        Assert.False(match.Ambiguous);
    }

    [Fact]
    public void Match_BreaksATieByTheGuideTagThenPageOrder()
    {
        HeroGuide guide = BuildFixtures.Guide(
            BuildFixtures.Build("Situational Build", GuideTags.Situational, AllFirst),
            BuildFixtures.Build("Recommended Build", GuideTags.Recommended, AllFirst),
            BuildFixtures.Build("Second Recommended Build", GuideTags.Recommended, AllFirst)
        );

        BuildMatch match = new BuildMatcher().Match(
            Played("T01", "T11", "T21", "T31"),
            guide,
            aram: false
        );

        Assert.Equal("Recommended Build", match.Best.Build.Name);
        Assert.True(match.Ambiguous);
    }

    [Fact]
    public void Match_InAramUsesOnlyTheAramBuilds_AndElsewhereSkipsThem()
    {
        HeroGuide guide = BuildFixtures.Guide(
            BuildFixtures.Build("ARAM Build", GuideTags.Aram, AllFirst),
            BuildFixtures.Build(
                "Artillery Build",
                GuideTags.Recommended,
                "nyn",
                "nyn",
                "nyn",
                "ny",
                "nyn",
                "nyn",
                "nynn"
            )
        );
        PlayedBuild played = Played("T01", "T11", "T21", "T31");

        Assert.Equal(
            "ARAM Build",
            new BuildMatcher().Match(played, guide, aram: true).Best.Build.Name
        );
        BuildMatch storm = new BuildMatcher().Match(played, guide, aram: false);
        Assert.Equal("Artillery Build", storm.Best.Build.Name);
        Assert.Null(storm.RunnerUp);
        Assert.Equal(0, storm.Best.Score, 3);
    }

    [Fact]
    public void Match_WithFewerTiersThanTheMinimum_IsNull()
    {
        HeroGuide guide = BuildFixtures.Guide(
            BuildFixtures.Build("Teleport Build", GuideTags.Recommended, AllFirst)
        );

        Assert.Null(new BuildMatcher().Match(Played("T01", "T11", "T21"), guide, aram: false));
        Assert.Null(
            new BuildMatcher().Match(Played("T01", "T11", "T21", "T31"), null, aram: false)
        );
        Assert.NotNull(
            new BuildMatcher(new BuildMatchOptions { MinTiers = 3 }).Match(
                Played("T01", "T11", "T21"),
                guide,
                aram: false
            )
        );
    }

    [Fact]
    public void Score_SkipsAStaleTier()
    {
        GuideBuild build = BuildFixtures.Build("Teleport Build", GuideTags.Recommended, AllFirst);
        build = build with
        {
            Tiers = new[]
            {
                build.Tiers[0] with
                {
                    Stale = true,
                },
                build.Tiers[1],
                build.Tiers[2],
                build.Tiers[3],
                build.Tiers[4],
                build.Tiers[5],
                build.Tiers[6],
            },
        };

        BuildScore score = new BuildMatcher().Score(
            Played("T02", "T11", "T21", "T31", "T41"),
            build
        );

        Assert.Equal(4, score.Tiers);
        Assert.Equal(1, score.Score, 3);
    }

    private static PlayedBuild Played(params string[] talentIds) =>
        PlayedBuilds.Read(BuildFixtures.Hero(), talentIds);
}
