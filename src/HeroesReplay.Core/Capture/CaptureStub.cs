using Microsoft.Extensions.Logging;

using System;
using System.Drawing;

namespace HeroesReplay.Core.Processes
{
    public class CaptureStub : CaptureStrategy
    {
        public CaptureStub(ILogger<CaptureStrategy> logger) : base(logger)
        {

        }

        public override Bitmap Capture(IntPtr handle, Rectangle? region = null) => new Bitmap(0, 0);

        public override Rectangle GetDimensions(IntPtr handle) => Rectangle.Empty;
    }
}