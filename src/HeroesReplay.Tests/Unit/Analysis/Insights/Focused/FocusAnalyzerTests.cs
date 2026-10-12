using System;
using System.Collections.Generic;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.Analysis.Insights.Focused;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis.Insights.Focused;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class FocusAnalyzerTests
{
    [Fact]
    public void Top_IsTheHeroTheTimelineFollowedLongest_BetweenGatesAndTheCore()
    {
        ReplaySnapshot snapshot = InsightFixtures.Snapshot(
            new[] { InsightFixtures.Player(0, 0, "Valla"), InsightFixtures.Player(7, 1, "Genji") }
        ) with
        {
            GatesOpen = TimeSpan.FromSeconds(10),
            End = TimeSpan.FromSeconds(100),
        };
        var timeline = new Dictionary<TimeSpan, Focus>();
        for (int second = 0; second < 120; second++)
        {
            timeline[TimeSpan.FromSeconds(second)] = new Focus(
                null,
                null,
                null,
                1,
                "test",
                second < 70 ? 7 : 0
            );
        }

        FocusInsights focus = new FocusAnalyzer().Analyze(snapshot, timeline);

        Assert.Equal("Genji", focus.Top.Hero);
        Assert.Equal(60, focus.Top.Seconds);
        Assert.Equal(60 / 90.0, focus.Top.Share, 3);
        Assert.Equal(30, focus.Players[1].Seconds);
        Assert.Null(new FocusAnalyzer().Analyze(snapshot, null).Top);
    }
}
