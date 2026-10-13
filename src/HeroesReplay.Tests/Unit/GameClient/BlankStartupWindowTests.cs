using System;
using System.Drawing;
using HeroesClientSDK;
using HeroesReplay.Core.GameClient;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

/// <summary>
/// The blank startup window from the frame's pixels and memory, not OCR (#292): synthetic frames
/// measured on the 64x36 grid, and HeroesClientSDK samples of a client still starting.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class BlankStartupWindowTests
{
    /// <summary>A black 1920x1080 bitmap, measured through <see cref="BlankStartupWindow.Measure(Bitmap)"/>.</summary>
    internal static readonly WindowFrame Black = MeasureFilled(1920, 1080, Color.Black);

    private static readonly HeroesClientVersion Current = new(2, 57, 0, 98348);
    private static readonly TimeSpan Young = TimeSpan.FromSeconds(20);

    private static WindowFrame MeasureFilled(int width, int height, Color color)
    {
        using var bitmap = new Bitmap(width, height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(color);
        }

        return BlankStartupWindow.Measure(bitmap);
    }

    private static WindowFrame Grid(int width, int height, Func<int, int, Color> pixel) =>
        BlankStartupWindow.Measure(width, height, (x, y) => pixel(x, y).ToArgb());

    private static ClientScreenSample Read(
        ClientScreenKind screen,
        string reason,
        bool menuSeen = false
    ) => new(screen, Array.Empty<string>(), menuSeen, reason, Current);

    [Fact]
    public void Measure_ABlackFrameIsUniform()
    {
        WindowFrame black = Black;

        Assert.True(black.FullSize);
        Assert.True(black.Uniform);
        Assert.Equal(0, black.MeanLuma);
        Assert.Equal(0, black.LumaSpread);
        Assert.Equal(1, black.UniformShare);
    }

    [Fact]
    public void Measure_AWhiteOrGreyFrameIsUniform()
    {
        Assert.True(Grid(1280, 720, (_, _) => Color.White).Uniform);
        Assert.True(Grid(1280, 720, (_, _) => Color.FromArgb(40, 44, 52)).Uniform);
    }

    [Fact]
    public void Measure_ASmallMarkOnABlackFrameIsStillUniform()
    {
        // A cursor or a small spinner: under 2% of the frame.
        WindowFrame frame = Grid(
            1920,
            1080,
            (x, y) => x > 940 && x < 980 && y > 520 && y < 560 ? Color.White : Color.Black
        );

        Assert.True(frame.Uniform);
        Assert.True(frame.UniformShare >= BlankStartupWindow.UniformFloor);
    }

    [Fact]
    public void Measure_TheBootSplashOrAMenuIsNotUniform()
    {
        // A logo and "LOADING" over a dark background, a gradient, and a checkerboard.
        WindowFrame splash = Grid(
            1920,
            1080,
            (x, y) => x > 560 && x < 1360 && y > 300 && y < 700 ? Color.SteelBlue : Color.Black
        );
        WindowFrame gradient = Grid(1920, 1080, (x, _) => Color.FromArgb(x * 255 / 1920, 0, 0));
        WindowFrame checker = Grid(
            1920,
            1080,
            (x, y) => (x / 30 + y / 30) % 2 == 0 ? Color.White : Color.Black
        );

        Assert.False(splash.Uniform);
        Assert.False(gradient.Uniform);
        Assert.False(checker.Uniform);
        Assert.True(checker.LumaSpread > 100);
    }

    [Fact]
    public void Measure_NoFrameIsNotUniform()
    {
        Assert.Equal(WindowFrame.None, BlankStartupWindow.Measure(null));
        Assert.False(WindowFrame.None.Uniform);
        Assert.False(WindowFrame.None.FullSize);
        Assert.Equal("no frame", WindowFrame.None.ToString());
    }

    [Fact]
    public void NoScreenYet_OnlyBeforeTheClientShowsAScreen()
    {
        // A client still unpacking its code, no menu root yet, frames not built, or no screen
        // shown, before any menu or match: no screen yet.
        Assert.True(BlankStartupWindow.NoScreenYet(null));
        Assert.True(
            BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.Unknown, "unsupported-build"))
        );
        Assert.True(BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.Unknown, "no-state")));
        Assert.True(BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.Unknown, "starting")));
        Assert.True(BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.NoScreen, "no-screen")));

        // The boot splash, a menu, a dialog, a loading screen or anything after a menu is a screen.
        Assert.False(BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.Splash, "screens")));
        Assert.False(BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.Home, "screens", true)));
        Assert.False(BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.Download, "screens")));
        Assert.False(BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.MapLoading, "screens")));
        Assert.False(
            BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.NoScreen, "no-screen", true))
        );
        Assert.False(
            BlankStartupWindow.NoScreenYet(Read(ClientScreenKind.Unknown, "read-failed", true))
        );
    }

    [Fact]
    public void IsBlank_AUniformFullSizeFrameWithNoScreenYetOnAYoungProcess()
    {
        ClientScreenSample starting = Read(ClientScreenKind.Unknown, "no-state");

        Assert.True(BlankStartupWindow.IsBlank(Black, starting, Young));
        Assert.True(BlankStartupWindow.IsBlank(Black, null, null));
    }

    [Fact]
    public void IsBlank_NotWhenMemoryShowsAScreen()
    {
        // A client at home whose window captures black (not in front) is home, not startup.
        Assert.False(
            BlankStartupWindow.IsBlank(
                Black,
                Read(ClientScreenKind.Home, "screens", menuSeen: true),
                Young
            )
        );
        // A black frame between two screens after a menu is not startup either.
        Assert.False(
            BlankStartupWindow.IsBlank(
                Black,
                Read(ClientScreenKind.NoScreen, "no-screen", menuSeen: true),
                Young
            )
        );
    }

    [Fact]
    public void IsBlank_NotForAnOldProcessASmallWindowOrAPicture()
    {
        ClientScreenSample starting = Read(ClientScreenKind.Unknown, "starting");

        Assert.False(BlankStartupWindow.IsBlank(Black, starting, BlankStartupWindow.StartupAge));
        Assert.False(
            BlankStartupWindow.IsBlank(Grid(404, 143, (_, _) => Color.Black), starting, Young)
        );
        Assert.False(
            BlankStartupWindow.IsBlank(
                Grid(1920, 1080, (x, y) => (x / 30 + y / 30) % 2 == 0 ? Color.White : Color.Black),
                starting,
                Young
            )
        );
        Assert.Equal(ClientRelaunch.GameDataStartupCap, BlankStartupWindow.StartupAge);
    }
}
