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
            return null;

        try
        {
            Rectangle client = GetDimensions(handle);
            if (client.Width <= 0 || client.Height <= 0)
            {
                return null;
            }

            using Bitmap full = new Bitmap(
                client.Width,
                client.Height,
                PixelFormat.Format32bppArgb
            );
            using (Graphics graphics = Graphics.FromImage(full))
            {
                IntPtr hdc = graphics.GetHdc();
                try
                {
                    if (!NativeMethods.PrintWindow(handle, hdc, PwRenderFullContent))
                    {
                        Logger.LogDebug("PrintWindow failed for handle {Handle}", handle);
                        return CaptureWithBitBlt(handle, region ?? client);
                    }
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }

            if (region == null)
            {
                return (Bitmap)full.Clone();
            }

            Rectangle crop = Rectangle.Intersect(
                region.Value,
                new Rectangle(0, 0, full.Width, full.Height)
            );
            if (crop.Width <= 0 || crop.Height <= 0)
            {
                return null;
            }

            return full.Clone(crop, full.PixelFormat);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Could not capture handle: {Handle}", handle);
            return CaptureWithBitBlt(handle, region);
        }
    }

    private Bitmap CaptureWithBitBlt(IntPtr handle, Rectangle? region)
    {
        Bitmap bitmap = null;
        Graphics source = null;
        Graphics destination = null;
        IntPtr deviceContextSource = IntPtr.Zero;
        IntPtr deviceContextDestination = IntPtr.Zero;

        try
        {
            Rectangle bounds = region ?? GetDimensions(handle);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return null;
            }

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
            Logger.LogWarning(e, "BitBlt fallback failed for handle: {Handle}", handle);
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
