using System;
using System.Drawing;
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
            return null;

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
                bitmap = null;
                return null;
            }

            return bitmap;
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Could not capture handle: {Handle}", handle);
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
