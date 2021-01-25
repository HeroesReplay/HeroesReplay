using Microsoft.Extensions.Logging;
using PInvoke;
using System;
using System.Drawing;

namespace HeroesReplay.Core.Processes
{
    public class CaptureBitBlt : CaptureStrategy
    {
        public CaptureBitBlt(ILogger<CaptureBitBlt> logger) : base(logger)
        {

        }

        public override Bitmap Capture(IntPtr handle, Rectangle? region = null)
        {
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
            catch (Exception)
            {
                Logger.LogWarning($"Could not capture handle: {handle}");
                throw;
            }
        }
    }
}