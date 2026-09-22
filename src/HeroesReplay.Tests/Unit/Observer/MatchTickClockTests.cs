using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchTickClockTests
{
    private const float Scale = 1f / 4096f;

    [Fact]
    public void TrySeconds_OneSecondIs4096Ticks()
    {
        Assert.True(MatchTickClock.TrySeconds(4096, Scale, out double seconds));
        Assert.Equal(1, seconds, precision: 2);
    }

    [Fact]
    public void TrySeconds_KeepsThePreGameSign()
    {
        Assert.True(MatchTickClock.TrySeconds(-4096 * 7, Scale, out double seconds));
        Assert.Equal(-7, seconds, precision: 2);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void TrySeconds_RejectsANonPositiveScale(float speed)
    {
        Assert.False(MatchTickClock.TrySeconds(4096, speed, out _));
    }

    [Fact]
    public void TrySeconds_RejectsAValuePastNinetyMinutes()
    {
        int ticks = 4096 * (90 * 60 + 1);
        Assert.False(MatchTickClock.TrySeconds(ticks, Scale, out _));
    }

    [Theory]
    [InlineData("2.55.17.98025", true)]
    [InlineData("2.55.17.97771", false)]
    [InlineData("", false)]
    public void IsSupportedVersion_IsOnlyBuild98025(string version, bool supported)
    {
        Assert.Equal(supported, MatchTickClock.IsSupportedVersion(version));
    }

    [Fact]
    public void Rvas_MatchThe98025GhidraNotes()
    {
        Assert.Equal(0x338D4A4, MatchTickClock.MatchTickRva);
        Assert.Equal(0x264862C, MatchTickClock.GameSpeedFactorRva);
    }
}
