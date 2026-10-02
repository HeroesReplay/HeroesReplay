using System.Drawing;
using HeroesReplay.Core.Spectating.Clock.Ocr;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Clock;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HudTimerCropTests
{
    [Fact]
    public void ForClient_1920x1080_CoversTheCenterClockAndSkipsTheFortScores()
    {
        Rectangle crop = HudTimerCrop.ForClient(1920, 1080);

        // The box that read the clock on the stream PC before #142: {X=900,Y=18,Width=120,Height=34}.
        Assert.Equal(new Rectangle(900, 18, 120, 34), crop);
        Assert.True(crop.Contains(930, 25));
        Assert.True(crop.Contains(994, 41));
        Assert.True(crop.Contains(1920 / 2, 33));
        Assert.False(crop.Contains(888, 32));
        Assert.False(crop.Contains(1034, 32));
    }

    [Fact]
    public void ForClient_DoesNotMoveTheDpiAwareClientOffTheClock()
    {
        // #142 made the process DPI aware. A 1280x720 reference scaled this 1920x1080 client
        // by 1.5 to {X=1350,Y=27,Width=180,Height=51}, right of the clock, and every replay timed out.
        Rectangle crop = HudTimerCrop.ForClient(1920, 1080);

        Assert.NotEqual(new Rectangle(1350, 27, 180, 51), crop);
        Assert.True(crop.Left < 1920 / 2 && crop.Right > 1920 / 2);
    }

    [Fact]
    public void ForClient_ScalesTheSameHudFractionTo2560x1440()
    {
        Rectangle source = HudTimerCrop.ForClient(1920, 1080);
        Rectangle scaled = HudTimerCrop.ForClient(2560, 1440);

        Assert.Equal(source.X * 2560 / 1920, scaled.X);
        Assert.Equal(source.Y * 1440 / 1080, scaled.Y);
        Assert.True(scaled.Contains(2560 / 2, 33 * 1440 / 1080));
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, 0)]
    [InlineData(-1, 720)]
    [InlineData(1280, -5)]
    public void ForClient_EmptyWhenTheClientHasNoArea(int width, int height)
    {
        Assert.Equal(Rectangle.Empty, HudTimerCrop.ForClient(width, height));
    }

    [Fact]
    public void ForClient_StaysInsideAClientSmallerThanTheReference()
    {
        Rectangle crop = HudTimerCrop.ForClient(50, 40);

        Assert.True(crop.Width > 0);
        Assert.True(crop.Height > 0);
        Assert.InRange(crop.X, 0, 49);
        Assert.InRange(crop.Y, 0, 39);
        Assert.InRange(crop.Right, 1, 50);
        Assert.InRange(crop.Bottom, 1, 40);
    }
}
