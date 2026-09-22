using System;
using System.Drawing;
using PInvoke;

namespace HeroesReplay.Core.Services.Observer;

public interface IGameCapture
{
    Bitmap Capture(IntPtr handle, Rectangle? region = null);

    Rectangle GetClientSize(IntPtr handle) => GameWindow.ClientSize(handle);
}

public static class GameWindow
{
    public static Rectangle ClientSize(IntPtr handle)
    {
        User32.GetClientRect(handle, out RECT rect);
        return new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }
}
