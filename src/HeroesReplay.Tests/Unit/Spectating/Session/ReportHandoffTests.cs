using System;
using HeroesReplay.Core.Spectating.Session;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

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

    /// <summary>#411: a Blind Rating replay's loading screen names the players.</summary>
    [Fact]
    public void ShouldCutReport_BlindLoadingScreen_KeepsTheReport()
    {
        Assert.False(
            ReportHandoff.ShouldCutReport(mapLoading: true, matchClock: null, blind: true)
        );
        Assert.False(
            ReportHandoff.ShouldCutReport(mapLoading: false, matchClock: null, blind: true)
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldCutReport_BlindMatchClock_StopsTheReport(bool mapLoading)
    {
        Assert.True(
            ReportHandoff.ShouldCutReport(mapLoading, TimeSpan.FromSeconds(3), blind: true)
        );
    }
}
