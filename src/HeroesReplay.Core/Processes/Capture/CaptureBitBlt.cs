using System;
using System.Drawing;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Processes
{
    public class CaptureBitBlt : CaptureStrategy
    {
        public CaptureBitBlt(ILogger<CaptureBitBlt> logger) : base(logger)
        {

        }

        public override Bitmap Capture(IntPtr handle, Rectangle? region = null)
        {
            DateTime start = DateTime.Now;

            Rectangle bounds = region ?? GetDimensions(handle);

            using (Graphics source = Graphics.FromHwnd(handle))
            {
                Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, source);

                using (Graphics destination = Graphics.FromImage(bitmap))
                {
                    IntPtr deviceContextSource = source.GetHdc();
                    IntPtr deviceContextDestination = destination.GetHdc();

                    NativeMethods.BitBlt(
                        deviceContextDestination, 0, 0, bounds.Width, bounds.Height,
                        deviceContextSource, bounds.Left, bounds.Top,
                        TernaryRasterOperations.SRCCOPY);

                    source.ReleaseHdc(deviceContextSource);
                    destination.ReleaseHdc(deviceContextDestination);
                }

                Logger.LogDebug("capture time: " + (DateTime.Now - start));

                return bitmap;
            }
        }
    }
}