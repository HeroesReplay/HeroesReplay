using System;
using System.Drawing;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Processes
{
    public abstract class CaptureStrategy
    {
        protected readonly ILogger<CaptureStrategy> Logger;

        protected CaptureStrategy(ILogger<CaptureStrategy> logger)
        {
            this.Logger = logger;
        }

        public abstract Bitmap Capture(IntPtr handle, Rectangle? region = null);

        public virtual Rectangle GetDimensions(IntPtr handle)
        {
            NativeMethods.GetClientRect(handle, out RECT rect);
            
            var rectangle = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

            Logger.LogDebug("window dimensions: " +  rectangle.Width + "x" + rectangle.Height);

            return rectangle;
        }
    }
}