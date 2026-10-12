using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Lanes;
using HeroesReplay.Core.YouTube.Metadata.Highlights;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata.Highlights;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HighlightPickerTests
{
    [Fact]
    public void Pick_TakesTheFirstKindInTheOrderTheMatchHas()
    {
        Highlight deathless = Pick(HighlightFixtures.Insights());
        Assert.Equal(HighlightKind.DeathlessCarry, deathless.Kind);
        Assert.Equal(
            new[]
            {
                "Deathless Teleport Li-Ming 12/0/9",
                "Deathless Li-Ming 12/0/9",
                "Deathless Li-Ming",
            },
            deathless.Variants
        );
        Assert.Equal("Li-Ming", deathless.Hero);

        Assert.Equal(
            HighlightKind.CoreRace,
            Pick(HighlightFixtures.Insights(coreRace: true, comeback: true)).Kind
        );
        Assert.Equal("Insane comeback", Pick(HighlightFixtures.Insights(comeback: true)).Text);
        Assert.Equal(
            "Valla pentakill",
            HighlightPicker
                .Default.Pick(
                    HighlightFixtures.Context(
                        HighlightFixtures.Insights(coreRace: true),
                        new[] { HighlightFixtures.Pentakill("Valla") }
                    )
                )
                .Text
        );
    }

    [Fact]
    public void Pick_FallsBackToTheMvpThenTheMostFollowedHero()
    {
        Highlight mvp = Pick(HighlightFixtures.Insights(deathless: false));
        Assert.Equal(HighlightKind.Mvp, mvp.Kind);
        Assert.Equal("MVP Orphea 7/1/5", mvp.Text);

        var settings = new HighlightSettings { Enabled = true, Order = new[] { "MostFollowed" } };
        Highlight followed = HighlightPicker.Default.Pick(
            HighlightFixtures.Context(HighlightFixtures.Insights(), settings: settings)
        );
        Assert.Equal(new[] { "Valla 3/4/10", "Valla" }, followed.Variants);
    }

    [Theory]
    [InlineData(0.84, "Teleport Build", false)]
    [InlineData(0.9, "Teleport Build", true)]
    [InlineData(1.0, "Standard Build", false)]
    [InlineData(1.0, "Basic Attacks Build", false)]
    public void ABuildIsNamedOnlyWhenStrongClearAndDistinctive(
        double score,
        string build,
        bool ambiguous
    )
    {
        Highlight highlight = Pick(
            HighlightFixtures.Insights(buildScore: score, buildName: build, ambiguous: ambiguous)
        );

        Assert.Equal("Deathless Li-Ming 12/0/9", highlight.Text);
    }

    [Fact]
    public void SoloLane_NeedsHighConfidence_AndNamesBothLanersWhenBothAreSure()
    {
        var settings = new HighlightSettings { Enabled = true, Order = new[] { "SoloLane" } };

        Assert.Equal(
            "Solo lane Muradin vs Thrall",
            HighlightPicker
                .Default.Pick(
                    HighlightFixtures.Context(
                        HighlightFixtures.Insights(
                            blueLane: LaneConfidence.High,
                            redLane: LaneConfidence.High
                        ),
                        settings: settings
                    )
                )
                .Text
        );
        Highlight one = HighlightPicker.Default.Pick(
            HighlightFixtures.Context(
                HighlightFixtures.Insights(
                    blueLane: LaneConfidence.High,
                    redLane: LaneConfidence.Medium
                ),
                settings: settings
            )
        );
        Assert.Equal("Solo laner Muradin", one.Text);
        Assert.Equal("Muradin", one.Hero);
        Assert.Null(
            HighlightPicker.Default.Pick(
                HighlightFixtures.Context(
                    HighlightFixtures.Insights(
                        blueLane: LaneConfidence.Medium,
                        redLane: LaneConfidence.Low
                    ),
                    settings: settings
                )
            )
        );
    }

    [Fact]
    public void CampDrought_NamesTheTeamAndCount_AndAKindLeftOutOfTheOrderIsNeverPicked()
    {
        var settings = new HighlightSettings { Enabled = true, Order = new[] { "CampDrought" } };
        Assert.Equal(
            "Red takes 1 camp",
            HighlightPicker
                .Default.Pick(
                    HighlightFixtures.Context(
                        HighlightFixtures.Insights(redCamps: 1),
                        settings: settings
                    )
                )
                .Text
        );
        Assert.Equal(
            "Red takes 0 camps",
            HighlightPicker
                .Default.Pick(
                    HighlightFixtures.Context(
                        HighlightFixtures.Insights(redCamps: 0),
                        settings: settings
                    )
                )
                .Text
        );
        Assert.Null(
            HighlightPicker.Default.Pick(
                HighlightFixtures.Context(HighlightFixtures.Insights(), settings: settings)
            )
        );
    }

    [Fact]
    public void Allowed_FiltersCandidates()
    {
        Highlight heroless = HighlightPicker.Default.Pick(
            HighlightFixtures.Context(HighlightFixtures.Insights(comeback: true)),
            candidate => candidate.Hero == null
        );

        Assert.Equal(HighlightKind.Comeback, heroless.Kind);
        Assert.Null(
            HighlightPicker.Default.Pick(
                HighlightFixtures.Context(HighlightFixtures.Insights()),
                candidate => candidate.Hero == null
            )
        );
        Assert.All(
            new HighlightSettings().Kinds(),
            kind => Assert.Contains(kind.ToString(), HighlightSettings.DefaultOrder)
        );
        Assert.Equal(
            HighlightSettings.DefaultOrder.Length,
            new HighlightSettings().Kinds().Count()
        );
    }

    private static Highlight Pick(Core.Analysis.Insights.MatchInsights insights) =>
        HighlightPicker.Default.Pick(HighlightFixtures.Context(insights));
}
