using System.Collections.Generic;
using HeroesReplay.Core.Services.Analysis;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class KillStreakTests
{
    [Fact]
    public void Group_ChainsKillsWithinWindowAcrossSeconds()
    {
        IReadOnlyList<KillStreak> streaks = KillStreaks.Group(
            new[] { 10, 12, 15, 18, 21 },
            windowSeconds: 12
        );

        Assert.Single(streaks);
        Assert.Equal(10, streaks[0].StartSecond);
        Assert.Equal(21, streaks[0].EndSecond);
        Assert.Equal(5, streaks[0].Kills);
    }

    [Fact]
    public void Group_SplitsWhenGapExceedsWindow()
    {
        IReadOnlyList<KillStreak> streaks = KillStreaks.Group(
            new[] { 10, 11, 40, 41 },
            windowSeconds: 12
        );

        Assert.Equal(2, streaks.Count);
        Assert.Equal(2, streaks[0].Kills);
        Assert.Equal(2, streaks[1].Kills);
        Assert.Equal(40, streaks[1].StartSecond);
    }

    [Fact]
    public void Group_SameSecondCountsAsStreak()
    {
        IReadOnlyList<KillStreak> streaks = KillStreaks.Group(new[] { 8, 8, 8 }, windowSeconds: 12);

        Assert.Single(streaks);
        Assert.Equal(3, streaks[0].Kills);
        Assert.Equal(8, streaks[0].StartSecond);
        Assert.Equal(8, streaks[0].EndSecond);
    }

    [Fact]
    public void Group_EmptyIsEmpty()
    {
        Assert.Empty(KillStreaks.Group(System.Array.Empty<int>(), 12));
    }
}
