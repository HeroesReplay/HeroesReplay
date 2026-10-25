using System.Collections.Generic;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Objectives;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Objectives;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObjectiveAnalyzerTests
{
    [Fact]
    public void TheLeaderWithABigShareAClearLeadAndTheAward_StandsOut()
    {
        ObjectiveInsights insights = Analyze(
            "Battlefield of Eternity",
            Immortal(0, "Valla", 52000, award: true),
            Immortal(1, "Muradin", 20000),
            Immortal(5, "Jaina", 18000),
            Immortal(6, "Thrall", 10000)
        );

        Assert.Equal("Immortal damage", insights.Label);
        Assert.True(insights.Standout);
        Assert.Equal("Valla", insights.Leader.Hero);
        Assert.Equal(52000, insights.Leader.Value);
        Assert.Equal(20000, insights.Leader.Next);
        Assert.Equal(0.52, insights.Leader.Share, 3);
        Assert.True(insights.Leader.Award);
    }

    [Theory]
    [InlineData(52000, 30000, 6000, true)] // 52% share, lead 1.7x
    [InlineData(30000, 15000, 15000, true)] // lead 2x, 33% share
    [InlineData(52000, 20000, 10000, false)] // 51% share, lead 2.6x, no award
    public void ALeaderShortOfAnyBar_IsNotAStandout(int top, int next, int others, bool award)
    {
        ObjectiveInsights insights = Analyze(
            "Battlefield of Eternity",
            Immortal(0, "Valla", top, award),
            Immortal(1, "Muradin", next),
            Immortal(5, "Jaina", others),
            Immortal(6, "Thrall", others),
            Immortal(7, "Diablo", others)
        );

        Assert.False(insights.Standout);
        Assert.Equal("Valla", insights.Leader.Hero);
    }

    [Fact]
    public void AMapWithoutAValue_HasNoLeader()
    {
        ObjectiveInsights insights = Analyze("Garden of Terror", Immortal(0, "Valla", 52000, true));

        Assert.Null(insights.Label);
        Assert.Null(insights.Leader);
        Assert.False(insights.Standout);
    }

    private static ObjectiveInsights Analyze(string map, params SnapshotPlayer[] players) =>
        new ObjectiveAnalyzer(new ObjectiveInsightSettings()).Analyze(
            InsightFixtures.Snapshot(players) with
            {
                Map = map,
            }
        );

    private static SnapshotPlayer Immortal(
        int index,
        string hero,
        int damage,
        bool award = false
    ) =>
        InsightFixtures.Player(
            index,
            index < 5 ? 0 : 1,
            hero,
            new PlayerScore
            {
                MapValues = new Dictionary<string, int> { ["DamageDoneToImmortal"] = damage },
                Awards = award ? new[] { "MostImmortalDamage" } : new string[0],
            }
        );
}
