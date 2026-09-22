using System;
using System.Drawing;

namespace HeroesReplay.Core.Services.Observer;

public sealed class StubCapture : IGameCapture
{
    public Bitmap Capture(IntPtr handle, Rectangle? region = null) => new Bitmap(1, 1);

    public Rectangle GetClientSize(IntPtr handle) => Rectangle.Empty;
}
