using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Copies the desktop at the game window's screen position. An opaque window covering
/// that rectangle is what gets captured. Prefer <see cref="PrintWindowCapture"/>.
/// </summary>
public sealed class BitBltCapture : IGameCapture
{
    private const int CaptureBlt = 0x40000000;
    private readonly ILogger<BitBltCapture> logger;

    public BitBltCapture(ILogger<BitBltCapture> logger)
    {
        this.logger = logger;
    }

    public Rectangle GetClientSize(IntPtr handle) => GameWindow.ClientSize(handle);

    public Bitmap Capture(IntPtr handle, Rectangle? region = null)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        Rectangle bounds = region ?? GetClientSize(handle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        var origin = new Native.POINT { X = bounds.Left, Y = bounds.Top };
        if (!Native.ClientToScreen(handle, ref origin))
        {
            return null;
        }

        Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        Graphics destination = null;
        IntPtr destDc = IntPtr.Zero;
        IntPtr desktop = IntPtr.Zero;
        try
        {
            destination = Graphics.FromImage(bitmap);
            destDc = destination.GetHdc();
            desktop = Native.GetDC(IntPtr.Zero);
            bool copied = Native.BitBlt(
                destDc,
                0,
                0,
                bounds.Width,
                bounds.Height,
                desktop,
                origin.X,
                origin.Y,
                (int)TernaryRasterOperation.SRCCOPY | CaptureBlt
            );
            if (!copied)
            {
                bitmap.Dispose();
                return null;
            }

            return bitmap;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Desktop BitBlt failed for handle {Handle}", handle);
            bitmap.Dispose();
            return null;
        }
        finally
        {
            if (destDc != IntPtr.Zero && destination != null)
            {
                destination.ReleaseHdc(destDc);
            }

            if (desktop != IntPtr.Zero)
            {
                Native.ReleaseDC(IntPtr.Zero, desktop);
            }

            destination?.Dispose();
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool BitBlt(
            IntPtr hdcDest,
            int x,
            int y,
            int width,
            int height,
            IntPtr hdcSrc,
            int xSrc,
            int ySrc,
            int rop
        );
    }
}
