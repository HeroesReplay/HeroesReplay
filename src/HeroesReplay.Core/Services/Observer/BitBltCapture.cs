using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

public class BitBltCapture : CaptureStrategy
{
    public BitBltCapture(ILogger<BitBltCapture> logger)
        : base(logger) { }

    public override Bitmap Capture(IntPtr handle, Rectangle? region = null)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        Rectangle bounds = region ?? GetDimensions(handle);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return null;
        }

        Bitmap bitmap = CaptureWindow(handle, bounds);
        if (bitmap != null && IsMostlyBlack(bitmap))
        {
            Logger.LogWarning(
                "Game window capture of client {Bounds} was empty/black. This reads the window frame, not the desktop.",
                bounds
            );
        }

        return bitmap;
    }

    /// <summary>
    /// Maps a client-area rectangle into a PrintWindow bitmap of the whole window.
    /// The window bitmap includes the title bar and borders. The desktop is not involved.
    /// </summary>
    public static bool TryMapClientToWindow(
        Rectangle windowBounds,
        Point clientOriginOnScreen,
        Rectangle clientRegion,
        out Rectangle crop
    )
    {
        crop = Rectangle.Empty;
        if (windowBounds.Width <= 0 || windowBounds.Height <= 0)
        {
            return false;
        }

        if (clientRegion.Width <= 0 || clientRegion.Height <= 0)
        {
            return false;
        }

        int x = clientOriginOnScreen.X - windowBounds.Left + clientRegion.Left;
        int y = clientOriginOnScreen.Y - windowBounds.Top + clientRegion.Top;
        if (
            x < 0
            || y < 0
            || x + clientRegion.Width > windowBounds.Width
            || y + clientRegion.Height > windowBounds.Height
        )
        {
            return false;
        }

        crop = new Rectangle(x, y, clientRegion.Width, clientRegion.Height);
        return true;
    }

    private Bitmap CaptureWindow(IntPtr handle, Rectangle clientRegion)
    {
        if (!NativeMethods.GetWindowRect(handle, out NativeMethods.RECT window))
        {
            return null;
        }

        var clientOrigin = new POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(handle, ref clientOrigin))
        {
            return null;
        }

        var windowBounds = new Rectangle(
            window.Left,
            window.Top,
            window.Right - window.Left,
            window.Bottom - window.Top
        );
        if (
            !TryMapClientToWindow(
                windowBounds,
                new Point(clientOrigin.X, clientOrigin.Y),
                clientRegion,
                out Rectangle crop
            )
        )
        {
            return null;
        }

        Bitmap windowBitmap = null;
        Graphics destination = null;
        IntPtr destDc = IntPtr.Zero;
        try
        {
            windowBitmap = new Bitmap(
                windowBounds.Width,
                windowBounds.Height,
                PixelFormat.Format32bppArgb
            );
            destination = Graphics.FromImage(windowBitmap);
            destDc = destination.GetHdc();
            // PW_RENDERFULLCONTENT asks DWM for the window's composed frame, including
            // DirectX, even when another window covers it on the desktop.
            if (!NativeMethods.PrintWindow(handle, destDc, NativeMethods.RenderFullContent))
            {
                windowBitmap.Dispose();
                return null;
            }

            destination.ReleaseHdc(destDc);
            destDc = IntPtr.Zero;
            return windowBitmap.Clone(crop, PixelFormat.Format32bppArgb);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Game window capture failed for handle {Handle}", handle);
            return null;
        }
        finally
        {
            if (destDc != IntPtr.Zero && destination != null)
            {
                destination.ReleaseHdc(destDc);
            }

            destination?.Dispose();
            windowBitmap?.Dispose();
        }
    }

    private static bool IsMostlyBlack(Bitmap bitmap)
    {
        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb
        );
        try
        {
            long sum = 0;
            int samples = 0;
            int stride = data.Stride;
            IntPtr scan0 = data.Scan0;
            for (int y = 0; y < data.Height; y += 8)
            {
                for (int x = 0; x < data.Width; x += 8)
                {
                    int offset = y * stride + x * 4;
                    byte b = Marshal.ReadByte(scan0, offset);
                    byte g = Marshal.ReadByte(scan0, offset + 1);
                    byte r = Marshal.ReadByte(scan0, offset + 2);
                    sum += r + g + b;
                    samples++;
                }
            }

            return samples == 0 || sum / samples < 12;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private static class NativeMethods
    {
        public const uint RenderFullContent = 0x2;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    }
}
