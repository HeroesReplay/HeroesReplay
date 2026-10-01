using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class NextReplayHoldTests
{
    [Fact]
    public void Duration_KeepsAMinuteAndTreatsANegativeAsTheDefault()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), NextReplayHold.Duration(TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.Zero, NextReplayHold.Duration(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMinutes(1), NextReplayHold.Duration(TimeSpan.FromMinutes(-5)));
        Assert.False(NextReplayHold.StopWhenReportEnds(TimeSpan.FromMinutes(1)));
        Assert.True(NextReplayHold.StopWhenReportEnds(TimeSpan.Zero));
    }
}
