using System;
using System.Drawing;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Processes
{
    public class CaptureFromScreen : CaptureStrategy
    {
        public CaptureFromScreen(ILogger<CaptureFromScreen> logger) : base(logger)
        {

        }

        public override Bitmap Capture(IntPtr handle, Rectangle? region = null)
        {
            DateTime start = DateTime.Now;

            Rectangle bounds = region ?? GetDimensions(handle);

            NativeMethods.SetForegroundWindow(handle);

            Bitmap result = new Bitmap(bounds.Width, bounds.Height);

            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            Logger.LogInformation("capture time: " + (DateTime.Now - start));

            return result;
        }
    }
}