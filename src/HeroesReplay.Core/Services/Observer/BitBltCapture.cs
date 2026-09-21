using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

public class BitBltCapture : CaptureStrategy
{
    private const int CaptureBlt = 0x40000000;

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

        Bitmap bitmap = CaptureClient(handle, bounds);
        if (bitmap != null && IsMostlyBlack(bitmap))
        {
            Logger.LogWarning(
                "BitBlt of screen {Bounds} was empty/black. Windowed mode is fine. A window covering this rectangle is what gets captured.",
                bounds
            );
        }

        return bitmap;
    }

    private Bitmap CaptureClient(IntPtr handle, Rectangle bounds)
    {
        Bitmap bitmap = null;
        Graphics destination = null;
        IntPtr desktop = IntPtr.Zero;
        IntPtr destDc = IntPtr.Zero;

        try
        {
            var origin = new POINT { X = bounds.Left, Y = bounds.Top };
            if (!NativeMethods.ClientToScreen(handle, ref origin))
            {
                return null;
            }

            bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            destination = Graphics.FromImage(bitmap);
            destDc = destination.GetHdc();
            desktop = NativeMethods.GetDC(IntPtr.Zero);

            bool copied = NativeMethods.BitBlt(
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
            Logger.LogWarning(e, "BitBlt failed for handle {Handle}", handle);
            bitmap?.Dispose();
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
                NativeMethods.ReleaseDC(IntPtr.Zero, desktop);
            }

            destination?.Dispose();
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
        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern bool BitBlt(
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
}
