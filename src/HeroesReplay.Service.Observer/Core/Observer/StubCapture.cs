using System;
using System.Drawing;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Service.Spectator.Core.Observer
{
    public class StubCapture : CaptureStrategy
    {
        public StubCapture(ILogger<CaptureStrategy> logger) : base(logger)
        {

        }

        public override Bitmap Capture(IntPtr handle, Rectangle? region = null) => new Bitmap(0, 0);

        public override Rectangle GetDimensions(IntPtr handle) => Rectangle.Empty;
    }
}