using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HudClockTests
{
    [Theory]
    [InlineData("-00:30", -30)]
    [InlineData("01:27", 87)]
    [InlineData("90:00", 5400)]
    public void TryParse_AcceptsMatchClock(string text, int totalSeconds)
    {
        Assert.True(HudClock.TryParse(text, out TimeSpan clock));
        Assert.Equal(totalSeconds, (int)clock.TotalSeconds);
    }

    [Theory]
    [InlineData("21.01:35:53")]
    [InlineData("1:02:03")]
    [InlineData("hh:mm:ss")]
    [InlineData("ELCOMEJ")]
    [InlineData("Defeat or bribe this camp")]
    [InlineData("91:00")]
    public void TryParse_RejectsAnythingElse(string text)
    {
        Assert.False(HudClock.TryParse(text, out _));
    }
}
