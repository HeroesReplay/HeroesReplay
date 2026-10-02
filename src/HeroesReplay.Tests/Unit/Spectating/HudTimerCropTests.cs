using System.Drawing;
using HeroesReplay.Core.Spectating.Clock.Ocr;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HudTimerCropTests
{
    [Fact]
    public void ForClient_1280x720_CoversTheScoreWellDigitsAndSkipsTheFortScores()
    {
        Rectangle crop = HudTimerCrop.ForClient(1280, 720);

        Assert.True(crop.Contains(930, 25));
        Assert.True(crop.Contains(994, 41));
        Assert.True(crop.Contains(962, 33));
        Assert.False(crop.Contains(888, 32));
        Assert.False(crop.Contains(1034, 32));
        Assert.NotEqual(new Rectangle(600, 9, 80, 36), crop);
        Assert.True(crop.X > 1280 / 2);
        Assert.InRange(crop.Right, 1, 1280);
        Assert.InRange(crop.Bottom, 1, 720);
    }

    [Fact]
    public void ForClient_ScalesTheSameHudFractionTo1920x1080()
    {
        Rectangle source = HudTimerCrop.ForClient(1280, 720);
        Rectangle scaled = HudTimerCrop.ForClient(1920, 1080);

        Assert.Equal(source.X * 1920 / 1280, scaled.X);
        Assert.Equal(source.Y * 1080 / 720, scaled.Y);
        Assert.Equal(source.Width * 1920 / 1280, scaled.Width);
        Assert.Equal(source.Height * 1080 / 720, scaled.Height);
        Assert.True(scaled.Contains(962 * 1920 / 1280, 33 * 1080 / 720));
        Assert.False(scaled.Contains(888 * 1920 / 1280, 32 * 1080 / 720));
        Assert.False(scaled.Contains(1034 * 1920 / 1280, 32 * 1080 / 720));
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
