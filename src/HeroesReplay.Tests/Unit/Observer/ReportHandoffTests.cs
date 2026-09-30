using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReportHandoffTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(120)]
    public void ShouldCutReport_RunningClock_StopsTheReport(int seconds)
    {
        Assert.True(ReportHandoff.ShouldCutReport(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void ShouldCutReport_DraftCountdown_KeepsTheReport()
    {
        Assert.False(ReportHandoff.ShouldCutReport(TimeSpan.FromSeconds(-30)));
    }

    [Fact]
    public void ShouldCutReport_NoClock_KeepsTheReport()
    {
        Assert.False(ReportHandoff.ShouldCutReport(null));
    }
}
