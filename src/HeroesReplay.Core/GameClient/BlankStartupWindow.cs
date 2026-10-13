using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HeroesClientSDK;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// The captured game window's brightness, measured on a grid of
/// <see cref="BlankStartupWindow.GridWidth"/> by <see cref="BlankStartupWindow.GridHeight"/>
/// points (a downscaled frame). No text is read.
/// </summary>
/// <param name="Width">The frame's width in pixels, 0 when there is no frame.</param>
/// <param name="Height">The frame's height in pixels.</param>
/// <param name="MeanLuma">The mean luma of the grid, 0 to 255.</param>
/// <param name="LumaSpread">The luma's standard deviation over the grid.</param>
/// <param name="UniformShare">
/// The share of grid points within <see cref="BlankStartupWindow.NearMedian"/> of the median luma.
/// </param>
public readonly record struct WindowFrame(
    int Width,
    int Height,
    double MeanLuma,
    double LumaSpread,
    double UniformShare
)
{
    /// <summary>No frame: the window was not found or did not capture.</summary>
    public static WindowFrame None { get; } = new(0, 0, 0, 0, 0);

    /// <summary>A full-size game window (at least 1000x700), not a small dialog.</summary>
    public bool FullSize =>
        Width >= BlankStartupWindow.MinWidth && Height >= BlankStartupWindow.MinHeight;

    /// <summary>
    /// Nearly one colour: at least <see cref="BlankStartupWindow.UniformFloor"/> of the grid is
    /// within <see cref="BlankStartupWindow.NearMedian"/> of the median luma.
    /// </summary>
    public bool Uniform => Width > 0 && UniformShare >= BlankStartupWindow.UniformFloor;

    public override string ToString() =>
        Width == 0
            ? "no frame"
            : $"{Width}x{Height}, luma {MeanLuma:0.0} spread {LumaSpread:0.0}, {UniformShare:P0} near the median";
}

/// <summary>
/// A blank startup window (#292): the client process has a full-size window that shows nothing
/// yet. It is decided from the captured frame's pixels and from memory, never from OCR: the frame
/// is nearly one colour (<see cref="WindowFrame.Uniform"/>), HeroesClientSDK
/// <see cref="ClientScreen"/> shows no screen yet (<see cref="NoScreenYet"/>), and the process
/// started less than <see cref="StartupAge"/> ago. A blank window is startup: the launch keeps
/// waiting for it (<see cref="ClientRelaunch.KeepsWaitingForGameData"/>), and only one that stays
/// blank for <see cref="ClientRelaunch.BlankWindowLimit"/> on the replay's own build is recovered
/// (<see cref="ClientRelaunch.BlankLaunchIsBroken"/>).
/// </summary>
public static class BlankStartupWindow
{
    public const int MinWidth = 1000;
    public const int MinHeight = 700;
    public const int GridWidth = 64;
    public const int GridHeight = 36;

    /// <summary>How far from the median luma a grid point may be and still count as the same colour.</summary>
    public const int NearMedian = 12;

    /// <summary>The share of the grid that must be near the median for a uniform frame.</summary>
    public const double UniformFloor = 0.98;

    /// <summary>
    /// A window is a startup window only while its process is younger than this: the longest a
    /// launch waits for game data (<see cref="ClientRelaunch.GameDataStartupCap"/>).
    /// </summary>
    public static readonly TimeSpan StartupAge = ClientRelaunch.GameDataStartupCap;

    /// <summary>Measures <paramref name="frame"/>; <see cref="WindowFrame.None"/> when null.</summary>
    public static WindowFrame Measure(Bitmap frame)
    {
        if (frame == null || frame.Width <= 0 || frame.Height <= 0)
        {
            return WindowFrame.None;
        }

        var rectangle = new Rectangle(0, 0, frame.Width, frame.Height);
        BitmapData data = frame.LockBits(
            rectangle,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb
        );
        try
        {
            return Measure(
                frame.Width,
                frame.Height,
                (x, y) => Marshal.ReadInt32(data.Scan0, y * data.Stride + x * 4)
            );
        }
        finally
        {
            frame.UnlockBits(data);
        }
    }

    /// <summary>
    /// Measures a frame of <paramref name="width"/> by <paramref name="height"/> whose pixel at
    /// (x, y) is <paramref name="argb"/> (0xAARRGGBB), on the grid's points only.
    /// </summary>
    public static WindowFrame Measure(int width, int height, Func<int, int, int> argb)
    {
        if (width <= 0 || height <= 0 || argb == null)
        {
            return WindowFrame.None;
        }

        int count = GridWidth * GridHeight;
        double[] luma = new double[count];
        int at = 0;
        for (int row = 0; row < GridHeight; row++)
        {
            int y = (int)((row + 0.5) * height / GridHeight);
            for (int column = 0; column < GridWidth; column++)
            {
                int x = (int)((column + 0.5) * width / GridWidth);
                int pixel = argb(x, y);
                int red = (pixel >> 16) & 0xFF;
                int green = (pixel >> 8) & 0xFF;
                int blue = pixel & 0xFF;
                luma[at++] = 0.299 * red + 0.587 * green + 0.114 * blue;
            }
        }

        double mean = 0;
        foreach (double value in luma)
        {
            mean += value;
        }

        mean /= count;
        double variance = 0;
        foreach (double value in luma)
        {
            variance += (value - mean) * (value - mean);
        }

        double[] sorted = (double[])luma.Clone();
        Array.Sort(sorted);
        double median = sorted[count / 2];
        int near = 0;
        foreach (double value in luma)
        {
            if (Math.Abs(value - median) <= NearMedian)
            {
                near++;
            }
        }

        return new WindowFrame(
            width,
            height,
            mean,
            Math.Sqrt(variance / count),
            (double)near / count
        );
    }

    /// <summary>
    /// True when memory shows no screen yet: no read, a read that cannot tell (a client still
    /// unpacking its code, no menu root yet, frames not built) or no screen shown, in a process that
    /// has not shown a menu or a match. A boot splash, a menu, a dialog, a loading screen or a match
    /// is a screen.
    /// </summary>
    public static bool NoScreenYet(ClientScreenSample? sample)
    {
        if (sample is not ClientScreenSample read)
        {
            return true;
        }

        if (read.MenuSeen)
        {
            return false;
        }

        return !read.Ok || read.Screen == ClientScreenKind.NoScreen;
    }

    /// <summary>
    /// A blank startup window: a full-size uniform frame, no screen in memory yet, and a process
    /// younger than <see cref="StartupAge"/> (an unknown age counts as young).
    /// </summary>
    public static bool IsBlank(WindowFrame frame, ClientScreenSample? sample, TimeSpan? processAge)
    {
        return frame.FullSize
            && frame.Uniform
            && NoScreenYet(sample)
            && (processAge is not TimeSpan age || age < StartupAge);
    }
}
