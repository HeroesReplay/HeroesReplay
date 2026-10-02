using System;
using HeroesReplay.Core.Spectating.Session;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class NextReplayHoldTests
{
    [Fact]
    public void Duration_KeepsAMinuteAndTreatsANegativeAsTheDefault()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), NextReplayHold.Duration(TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.Zero, NextReplayHold.Duration(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromSeconds(90), NextReplayHold.Duration(TimeSpan.FromMinutes(-5)));
        Assert.False(NextReplayHold.StopWhenReportEnds(TimeSpan.FromMinutes(1)));
        Assert.True(NextReplayHold.StopWhenReportEnds(TimeSpan.Zero));
    }
}
