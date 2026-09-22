using System.Drawing;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class BitBltCaptureTests
{
    [Fact]
    public void TryMapClientToWindow_PlacesTheClockInsideTheFrame()
    {
        var window = new Rectangle(285, 76, 1936, 1119);
        var clientOnScreen = new Point(293, 107);
        var clock = new Rectangle(910, 14, 100, 48);

        bool mapped = PrintWindowCapture.TryMapClientToWindow(
            window,
            clientOnScreen,
            clock,
            out Rectangle crop
        );

        Assert.True(mapped);
        Assert.Equal(new Rectangle(918, 45, 100, 48), crop);
    }

    [Fact]
    public void TryMapClientToWindow_RejectsARegionOutsideTheWindow()
    {
        bool mapped = PrintWindowCapture.TryMapClientToWindow(
            new Rectangle(0, 0, 100, 100),
            new Point(0, 0),
            new Rectangle(90, 0, 20, 10),
            out Rectangle crop
        );

        Assert.False(mapped);
        Assert.Equal(Rectangle.Empty, crop);
    }
}
