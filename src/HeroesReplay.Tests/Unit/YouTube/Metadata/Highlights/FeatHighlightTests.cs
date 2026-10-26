using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Awards;
using HeroesReplay.Core.Analysis.Insights.Bursts;
using HeroesReplay.Core.Analysis.Insights.Objectives;
using HeroesReplay.Core.YouTube.Metadata.Highlights;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata.Highlights;

/// <summary>The #512 kinds: repeated triple kills, the objective standout, and awards.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FeatHighlightTests
{
    [Fact]
    public void TripleKill_NeedsTwoByOneHero()
    {
        var settings = new HighlightSettings { Enabled = true, Order = new[] { "TripleKill" } };

        Highlight twice = HighlightPicker.Default.Pick(
            HighlightFixtures.Context(WithBursts("Valla", 1, 2), settings: settings)
        );
        Assert.Equal(HighlightKind.TripleKill, twice.Kind);
        Assert.Equal(new[] { "Valla 2 triple kills" }, twice.Variants);
        Assert.Equal("Triple kill", twice.Tag);

        Assert.Null(
            HighlightPicker.Default.Pick(
                HighlightFixtures.Context(WithBursts("Valla", 1, 1), settings: settings)
            )
        );
    }

    [Fact]
    public void ObjectiveStandout_NamesTheShareRoundedDown_WithAStrongBuildFirst()
    {
        var settings = new HighlightSettings
        {
            Enabled = true,
            Order = new[] { "ObjectiveStandout" },
        };
        MatchInsights insights = HighlightFixtures.Insights() with
        {
            Objectives = Objective(standout: true),
        };

        Highlight highlight = HighlightPicker.Default.Pick(
            HighlightFixtures.Context(insights, settings: settings)
        );

        Assert.Equal(
            new[] { "Teleport Li-Ming 52% of Immortal damage", "Li-Ming 52% of Immortal damage" },
            highlight.Variants
        );
        Assert.Equal("Objective", highlight.Tag);

        Assert.Null(
            HighlightPicker.Default.Pick(
                HighlightFixtures.Context(
                    HighlightFixtures.Insights() with
                    {
                        Objectives = Objective(standout: false),
                    },
                    settings: settings
                )
            )
        );
    }

    [Fact]
    public void Award_NamesItsCount()
    {
        var settings = new HighlightSettings { Enabled = true, Order = new[] { "Award" } };
        MatchInsights insights = HighlightFixtures.Insights() with
        {
            Awards = new AwardInsights
            {
                Awards = new[]
                {
                    new PlayerAward
                    {
                        PlayerIndex = 6,
                        Team = 1,
                        Hero = "Thrall",
                        Kind = AwardKind.ClutchHealer,
                        Value = 9,
                    },
                },
            },
        };

        Highlight highlight = HighlightPicker.Default.Pick(
            HighlightFixtures.Context(insights, settings: settings)
        );

        Assert.Equal("Thrall 9 clutch heals", highlight.Text);
        Assert.Equal("Clutch healer", highlight.Tag);

        Highlight hatTrick = HighlightPicker.Default.Pick(
            HighlightFixtures.Context(
                insights with
                {
                    Awards = new AwardInsights
                    {
                        Awards = new[]
                        {
                            new PlayerAward
                            {
                                PlayerIndex = 6,
                                Hero = "Thrall",
                                Kind = AwardKind.HatTrick,
                                Value = 8,
                            },
                        },
                    },
                },
                settings: settings
            )
        );
        Assert.Equal("Thrall hat trick", hatTrick.Text);
        Assert.Equal("Hat trick", hatTrick.Tag);
    }

    [Fact]
    public void DefaultOrder_PutsAnAwardAboveTheKillStandouts_AndTheObjectiveBelowThem()
    {
        // The fixture's Li-Ming is a deathless carry.
        MatchInsights insights = HighlightFixtures.Insights() with
        {
            Objectives = Objective(standout: true),
        };
        Assert.Equal(
            HighlightKind.DeathlessCarry,
            HighlightPicker.Default.Pick(HighlightFixtures.Context(insights)).Kind
        );
        Assert.Equal(
            HighlightKind.ObjectiveStandout,
            HighlightPicker
                .Default.Pick(
                    HighlightFixtures.Context(
                        HighlightFixtures.Insights(deathless: false) with
                        {
                            Objectives = Objective(standout: true),
                        }
                    )
                )
                .Kind
        );

        MatchInsights awarded = insights with
        {
            Awards = new AwardInsights
            {
                Awards = new[]
                {
                    new PlayerAward
                    {
                        PlayerIndex = 6,
                        Hero = "Thrall",
                        Kind = AwardKind.ClutchHealer,
                        Value = 9,
                    },
                },
            },
        };
        Assert.Equal(
            HighlightKind.Award,
            HighlightPicker.Default.Pick(HighlightFixtures.Context(awarded)).Kind
        );
        Assert.Equal(
            HighlightKind.TripleKill,
            HighlightPicker
                .Default.Pick(
                    HighlightFixtures.Context(
                        awarded with
                        {
                            Bursts = new BurstInsights
                            {
                                Players = new[] { Bursts("Valla", 1, 2) },
                            },
                        }
                    )
                )
                .Kind
        );
    }

    [Fact]
    public void TheHighlightsLine_ListsEveryTripleKill_ButNotAPentakillHerosAgain()
    {
        MatchInsights insights = WithBursts("Valla", 1, 1) with
        {
            Bursts = new BurstInsights
            {
                Players = new[] { Bursts("Valla", 1, 1), Bursts("Orphea", 5, 2) },
            },
            Objectives = Objective(standout: true),
        };

        string line = HighlightLines.Highlights(
            HighlightFixtures.Context(insights),
            new[] { HighlightFixtures.Pentakill("Valla") }
        );

        Assert.Equal(
            "Highlights: Valla pentakill; Li-Ming 12/0/9 with no deaths; MVP Orphea 7/1/5; Orphea 2 triple kills; Li-Ming 52% of Immortal damage",
            line
        );
    }

    private static MatchInsights WithBursts(string hero, int playerIndex, int bursts) =>
        HighlightFixtures.Insights() with
        {
            Bursts = new BurstInsights { Players = new[] { Bursts(hero, playerIndex, bursts) } },
        };

    private static PlayerBursts Bursts(string hero, int playerIndex, int bursts) =>
        new()
        {
            PlayerIndex = playerIndex,
            Team = playerIndex < 5 ? 0 : 1,
            Hero = hero,
            Bursts = Enumerable.Range(0, bursts).Select(i => new Burst(300 + i * 200, 3)).ToArray(),
        };

    private static ObjectiveInsights Objective(bool standout) =>
        new()
        {
            Label = "Immortal damage",
            Standout = standout,
            Leader = new ObjectiveLeader
            {
                PlayerIndex = 0,
                Team = 0,
                Hero = "Li-Ming",
                Value = 52000,
                Next = 20000,
                Share = 0.529,
                Award = true,
            },
        };
}
