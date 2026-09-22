using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Captures the game window's composed frame, including DirectX, even when another
/// window covers it. This does not read the desktop.
/// </summary>
public sealed class PrintWindowCapture : IGameCapture
{
    private readonly ILogger<PrintWindowCapture> logger;

    public PrintWindowCapture(ILogger<PrintWindowCapture> logger)
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

        Rectangle clientRegion = region ?? GetClientSize(handle);
        if (clientRegion.Width <= 0 || clientRegion.Height <= 0)
        {
            return null;
        }

        Bitmap bitmap = CaptureWindow(handle, clientRegion);
        if (bitmap != null && IsMostlyBlack(bitmap))
        {
            logger.LogWarning(
                "Game window capture of client {Bounds} was empty/black.",
                clientRegion
            );
        }

        return bitmap;
    }

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
        if (!Native.GetWindowRect(handle, out Native.RECT window))
        {
            return null;
        }

        var clientOrigin = new Native.POINT();
        if (!Native.ClientToScreen(handle, ref clientOrigin))
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
            if (!Native.PrintWindow(handle, destDc, Native.RenderFullContent))
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
            logger.LogWarning(e, "Game window capture failed for handle {Handle}", handle);
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
                    sum +=
                        Marshal.ReadByte(scan0, offset)
                        + Marshal.ReadByte(scan0, offset + 1)
                        + Marshal.ReadByte(scan0, offset + 2);
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

    private static class Native
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

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    }
}
