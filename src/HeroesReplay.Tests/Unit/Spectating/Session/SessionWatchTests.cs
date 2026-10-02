using HeroesReplay.Core.Spectating.Session;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SessionWatchTests
{
    [Fact]
    public void IsHung_IgnoresAWindowWhileTheClockAdvances()
    {
        Assert.False(SessionWatch.IsHung(windowHung: true, clockAdvanced: true));
    }

    [Fact]
    public void IsHung_CountsAStuckClock()
    {
        Assert.True(SessionWatch.IsHung(windowHung: true, clockAdvanced: false));
    }

    [Fact]
    public void IsHung_IgnoresAResponsiveWindow()
    {
        Assert.False(SessionWatch.IsHung(windowHung: false, clockAdvanced: false));
        Assert.False(SessionWatch.IsHung(windowHung: false, clockAdvanced: true));
    }
}
