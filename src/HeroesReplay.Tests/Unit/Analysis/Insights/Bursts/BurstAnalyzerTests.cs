using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Bursts;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Bursts;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BurstAnalyzerTests
{
    [Fact]
    public void Bursts_AreEachPlayersOwnBlows_MostBurstsFirst()
    {
        ReplaySnapshot snapshot = InsightFixtures.Snapshot(
            new[]
            {
                InsightFixtures.Player(0, 0, "Valla"),
                InsightFixtures.Player(1, 0, "Kael'thas"),
                InsightFixtures.Player(5, 1, "Jaina"),
            }
        ) with
        {
            KillingBlows = new[]
            {
                // Valla: one triple kill at 7:50.
                new KillingBlow(470, 0),
                new KillingBlow(471, 0),
                new KillingBlow(473, 0),
                // Kael'thas: two, the second a quadra; Jaina's blow in between is hers.
                new KillingBlow(1094, 1),
                new KillingBlow(1095, 1),
                new KillingBlow(1095, 5),
                new KillingBlow(1096, 1),
                new KillingBlow(1276, 1),
                new KillingBlow(1277, 1),
                new KillingBlow(1278, 1),
                new KillingBlow(1279, 1),
            },
        };

        BurstInsights insights = new BurstAnalyzer(new BurstInsightSettings()).Analyze(snapshot);

        Assert.Equal(new[] { "Kael'thas", "Valla" }, insights.Players.Select(p => p.Hero));
        Assert.Equal(new[] { new Burst(1094, 3), new Burst(1276, 4) }, insights.Players[0].Bursts);
        Assert.Equal(new[] { new Burst(470, 3) }, insights.Players[1].Bursts);
    }

    [Fact]
    public void SpreadOutBlowsAndAiPlayers_AreNotBursts()
    {
        ReplaySnapshot snapshot = InsightFixtures.Snapshot(
            new[]
            {
                InsightFixtures.Player(0, 0, "Valla"),
                InsightFixtures.Player(5, 1, "Jaina", ai: true),
            }
        ) with
        {
            KillingBlows = new[]
            {
                new KillingBlow(100, 0),
                new KillingBlow(102, 0),
                new KillingBlow(104, 0),
                new KillingBlow(200, 5),
                new KillingBlow(200, 5),
                new KillingBlow(201, 5),
            },
        };

        Assert.Empty(new BurstAnalyzer(null).Analyze(snapshot).Players);
    }
}
