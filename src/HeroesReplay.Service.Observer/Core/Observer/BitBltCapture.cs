using System;
using System.Drawing;
using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;
using PInvoke;

namespace HeroesReplay.Service.Spectator.Core.Observer
{
    public class BitBltCapture : CaptureStrategy
    {
        public BitBltCapture(ILogger<BitBltCapture> logger) : base(logger)
        {

        }

        public override Bitmap Capture(IntPtr handle, Rectangle? region = null)
        {
            if (handle == IntPtr.Zero) return null;

            try
            {
                Rectangle bounds = region ?? GetDimensions(handle);

                using (Graphics source = Graphics.FromHwnd(handle))
                {
                    Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, source);

                    using (Graphics destination = Graphics.FromImage(bitmap))
                    {
                        IntPtr deviceContextSource = source.GetHdc();
                        IntPtr deviceContextDestination = destination.GetHdc();

                        Gdi32.BitBlt(
                            deviceContextDestination, 0, 0, bounds.Width, bounds.Height,
                            deviceContextSource, bounds.Left, bounds.Top,
                            (int)TernaryRasterOperation.SRCCOPY);

                        source.ReleaseHdc(deviceContextSource);

                        destination.ReleaseHdc(deviceContextDestination);
                    }

                    return bitmap;
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, $"Could not capture handle: {handle}");
            }

            return null;
        }
    }
}