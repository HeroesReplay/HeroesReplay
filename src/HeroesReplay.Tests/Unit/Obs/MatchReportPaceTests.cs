using System;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchReportPaceTests
{
    [Fact]
    public void ScrollSpeed_KeepsTheSameDistanceWhenTheReportIsShorter()
    {
        double speed = MatchReportPace.ScrollSpeedY(TimeSpan.FromMinutes(2));

        Assert.Equal(51, speed, 3);
        Assert.Equal(
            MatchReportPace.ReferenceSpeedY * MatchReportPace.ReferenceDuration.TotalSeconds,
            speed * 120,
            3
        );
    }
}
