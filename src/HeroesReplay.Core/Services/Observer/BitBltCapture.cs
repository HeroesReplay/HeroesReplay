using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

public class BitBltCapture : CaptureStrategy
{
    private const uint PwRenderFullContent = 0x00000002;

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

        Bitmap bitBlt = CaptureWithBitBlt(handle, bounds);
        if (bitBlt != null && !IsMostlyBlack(bitBlt))
        {
            return bitBlt;
        }

        bitBlt?.Dispose();
        Logger.LogDebug("BitBlt of {Bounds} was empty/black; using PrintWindow.", bounds);
        return CaptureWithPrintWindow(handle, bounds);
    }

    private Bitmap CaptureWithBitBlt(IntPtr handle, Rectangle bounds)
    {
        Bitmap bitmap = null;
        Graphics source = null;
        Graphics destination = null;
        IntPtr deviceContextSource = IntPtr.Zero;
        IntPtr deviceContextDestination = IntPtr.Zero;

        try
        {
            source = Graphics.FromHwnd(handle);
            bitmap = new Bitmap(bounds.Width, bounds.Height, source);
            destination = Graphics.FromImage(bitmap);

            deviceContextSource = source.GetHdc();
            deviceContextDestination = destination.GetHdc();

            bool copied = NativeMethods.BitBlt(
                deviceContextDestination,
                0,
                0,
                bounds.Width,
                bounds.Height,
                deviceContextSource,
                bounds.Left,
                bounds.Top,
                (int)TernaryRasterOperation.SRCCOPY
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
            if (deviceContextSource != IntPtr.Zero)
            {
                source.ReleaseHdc(deviceContextSource);
            }

            if (deviceContextDestination != IntPtr.Zero)
            {
                destination.ReleaseHdc(deviceContextDestination);
            }

            destination?.Dispose();
            source?.Dispose();
        }
    }

    private Bitmap CaptureWithPrintWindow(IntPtr handle, Rectangle bounds)
    {
        Rectangle client = GetDimensions(handle);
        if (client.Width <= 0 || client.Height <= 0)
        {
            return null;
        }

        using Bitmap full = new Bitmap(client.Width, client.Height, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(full))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (!NativeMethods.PrintWindow(handle, hdc, PwRenderFullContent))
                {
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        Rectangle crop = Rectangle.Intersect(bounds, new Rectangle(0, 0, full.Width, full.Height));
        if (crop.Width <= 0 || crop.Height <= 0)
        {
            return null;
        }

        if (crop.Width == full.Width && crop.Height == full.Height && crop.X == 0 && crop.Y == 0)
        {
            return (Bitmap)full.Clone();
        }

        return full.Clone(crop, full.PixelFormat);
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

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

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
