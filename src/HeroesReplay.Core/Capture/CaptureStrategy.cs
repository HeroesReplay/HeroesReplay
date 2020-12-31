using Microsoft.Extensions.Logging;

using PInvoke;

using System;
using System.Drawing;

namespace HeroesReplay.Core.Processes
{
    public abstract class CaptureStrategy
    {
        private readonly ILogger<CaptureStrategy> logger;

        protected ILogger<CaptureStrategy> Logger => logger;

        protected CaptureStrategy(ILogger<CaptureStrategy> logger)
        {
            this.logger = logger;
        }

        public abstract Bitmap Capture(IntPtr handle, Rectangle? region = null);

        public virtual Rectangle GetDimensions(IntPtr handle)
        {
            User32.GetClientRect(handle, out RECT rect);

            var rectangle = new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);

            Logger.LogDebug("window dimensions: " + rectangle.Width + "x" + rectangle.Height);

            return rectangle;
        }
    }
}