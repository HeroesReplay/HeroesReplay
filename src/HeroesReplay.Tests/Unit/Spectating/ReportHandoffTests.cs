using System;
using HeroesReplay.Core.Spectating;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReportHandoffTests
{
    [Fact]
    public void ShouldCutReport_MapLoading_StopsTheReport()
    {
        Assert.True(ReportHandoff.ShouldCutReport(mapLoading: true, matchClock: null));
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(120)]
    public void ShouldCutReport_VisibleClock_StopsTheReport(int seconds)
    {
        Assert.True(
            ReportHandoff.ShouldCutReport(mapLoading: false, TimeSpan.FromSeconds(seconds))
        );
    }

    [Fact]
    public void ShouldCutReport_ClientStillStarting_KeepsTheReport()
    {
        Assert.False(ReportHandoff.ShouldCutReport(mapLoading: false, matchClock: null));
    }
}
