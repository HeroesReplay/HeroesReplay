using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchTimerFilterTests
{
    private static readonly TimeSpan Jump = TimeSpan.FromSeconds(8);

    [Fact]
    public void FirstSample_IsAlwaysPlausible()
    {
        var filter = new MatchTimerFilter();
        Assert.True(filter.IsPlausible(TimeSpan.FromMinutes(1), Jump));
    }

    [Fact]
    public void SmallForwardStep_IsPlausible()
    {
        var filter = new MatchTimerFilter();
        filter.Accept(TimeSpan.FromMinutes(10));
        Assert.True(
            filter.IsPlausible(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(2)), Jump)
        );
    }

    [Fact]
    public void HugeJump_IsRejected()
    {
        var filter = new MatchTimerFilter();
        filter.Accept(TimeSpan.FromMinutes(10));
        Assert.False(filter.IsPlausible(TimeSpan.FromDays(8), Jump));
        Assert.False(filter.IsPlausible(TimeSpan.FromMinutes(12), Jump));
    }

    [Fact]
    public void LargeRewind_IsRejected()
    {
        var filter = new MatchTimerFilter();
        filter.Accept(TimeSpan.FromMinutes(10));
        Assert.False(filter.IsPlausible(TimeSpan.FromMinutes(9), Jump));
    }

    [Fact]
    public void OneSecondRewind_IsAllowed()
    {
        var filter = new MatchTimerFilter();
        filter.Accept(TimeSpan.FromMinutes(10));
        Assert.True(
            filter.IsPlausible(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(-1)), Jump)
        );
    }
}
