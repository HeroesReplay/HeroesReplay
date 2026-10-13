using System;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.Analysis.Insights.Camps;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Camps;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class CampAnalyzerTests
{
    [Fact]
    public void Captures_CountTheEnemySideAndBosses_AndATeamWithNoneHasADrought()
    {
        ReplaySnapshot snapshot = InsightFixtures.Snapshot(Array.Empty<SnapshotPlayer>()) with
        {
            Camps = new[]
            {
                new Camp(1, new MapPoint(60, 150)),
                new Camp(2, new MapPoint(190, 150)),
                new Camp(3, new MapPoint(125, 40)),
            },
            CampCaptures = new[]
            {
                new CampCapture(2, "Siege Camp", 0, TimeSpan.FromMinutes(3)),
                new CampCapture(2, "Siege Camp", 0, TimeSpan.FromMinutes(8)),
                new CampCapture(1, "Bruiser Camp", 0, TimeSpan.FromMinutes(9)),
                new CampCapture(3, "Boss Camp", 0, TimeSpan.FromMinutes(12)),
            },
        };

        CampInsights camps = new CampAnalyzer(new CampInsightSettings()).Analyze(snapshot);

        Assert.Equal(4, camps.TotalCaptures);
        TeamCamps blue = camps.Teams[0];
        Assert.Equal(4, blue.Captures);
        Assert.Equal(2, blue.EnemySideCaptures);
        Assert.Equal(1, blue.BossCaptures);
        Assert.False(blue.Drought);
        Assert.True(camps.Teams[1].Drought);
        Assert.False(
            new CampAnalyzer(new CampInsightSettings())
                .Analyze(snapshot with { End = TimeSpan.FromMinutes(9) })
                .Teams[1]
                .Drought
        );
        Assert.Empty(
            new CampAnalyzer(new CampInsightSettings()).Analyze(snapshot with { Aram = true }).Teams
        );
    }
}
