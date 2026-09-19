using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WgcSharp;
using Xunit;
using WgcCaptureStrategy = WgcSharp.CaptureStrategy;

namespace HeroesReplay.Tests.Functional;

[Trait(TestCategories.Category, TestCategories.Functional)]
public class WindowsGraphicsCaptureTests
{
    private const string ProcessName = "HeroesOfTheStorm_x64";

    [Fact]
    public void WgcOnly_CapturesNonBlackFrame_OfLiveHotsWindow()
    {
        IntPtr hwnd = FindHotsWindow();
        Assert.True(
            hwnd != IntPtr.Zero,
            "Start Heroes of the Storm (windowed 1080p) before TestCategory=Functional."
        );

        using Bitmap wgc = WindowCapture.CaptureWindow(
            hwnd,
            WgcCaptureStrategy.WgcOnly,
            timeoutMs: 4000
        );

        Assert.NotNull(wgc);
        Assert.True(wgc.Width >= 1920, $"WGC width {wgc.Width}");
        Assert.True(wgc.Height >= 1080, $"WGC height {wgc.Height}");

        double avg = AverageRgb(wgc);
        Assert.True(avg > 12, $"WGC frame looks black (avgRGB={avg:0.0}).");
    }

    [Fact]
    public void WindowDcBitBlt_OfLiveHotsWindow_IsBlack()
    {
        IntPtr hwnd = FindHotsWindow();
        Assert.True(
            hwnd != IntPtr.Zero,
            "Start Heroes of the Storm (windowed 1080p) before TestCategory=Functional."
        );

        using Bitmap bitBlt = CaptureWindowDc(hwnd, 1920, 1080);
        Assert.NotNull(bitBlt);
        double avg = AverageRgb(bitBlt);
        Assert.True(
            avg < 12,
            $"Window-DC BitBlt was not black (avgRGB={avg:0.0}); DXGI flip assumption changed."
        );
    }

    private static IntPtr FindHotsWindow()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        try
        {
            foreach (Process process in processes)
            {
                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    return process.MainWindowHandle;
                }
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }

        return IntPtr.Zero;
    }

    private static double AverageRgb(Bitmap bitmap)
    {
        long sum = 0;
        int samples = 0;
        for (int y = 0; y < bitmap.Height; y += 8)
        {
            for (int x = 0; x < bitmap.Width; x += 8)
            {
                Color pixel = bitmap.GetPixel(x, y);
                sum += pixel.R + pixel.G + pixel.B;
                samples++;
            }
        }

        return samples == 0 ? 0 : (double)sum / samples;
    }

    private static Bitmap CaptureWindowDc(IntPtr hwnd, int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics destination = Graphics.FromImage(bitmap);
        IntPtr destDc = destination.GetHdc();
        IntPtr sourceDc = GetDC(hwnd);
        try
        {
            BitBlt(destDc, 0, 0, width, height, sourceDc, 0, 0, 0x00CC0020);
        }
        finally
        {
            destination.ReleaseHdc(destDc);
            ReleaseDC(hwnd, sourceDc);
        }

        return bitmap;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr hdcDest,
        int nXDest,
        int nYDest,
        int nWidth,
        int nHeight,
        IntPtr hdcSrc,
        int nXSrc,
        int nYSrc,
        int dwRop
    );
}
