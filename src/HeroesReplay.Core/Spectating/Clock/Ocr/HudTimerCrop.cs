using System.Drawing;

namespace HeroesReplay.Core.Spectating.Clock.Ocr;

/// <summary>
/// Client-pixel crop of the observer score-well clock.
/// Measured on the 1920x1080 client: the MM:SS glyphs occupy x=930..994, y=25..41, the
/// horizontal center. The fort scores sit just outside that, at about x=884..892 and x=1030..1038.
/// Those pixels were first read from a DPI-virtualized PrintWindow that returned the top-left
/// 1280x720 of the real 1920x1080 frame, not a downscaled one. Since the process is per-monitor
/// DPI aware (#142) the client reads as 1920x1080, so that is the reference size.
/// </summary>
public static class HudTimerCrop
{
    public const int ReferenceWidth = 1920;
    public const int ReferenceHeight = 1080;

    public static Rectangle ForClient(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return Rectangle.Empty;
        }

        int start = width * 900 / ReferenceWidth;
        int top = height * 18 / ReferenceHeight;
        int cropWidth = width * 120 / ReferenceWidth;
        int cropHeight = height * 34 / ReferenceHeight;
        if (cropWidth < 1)
        {
            cropWidth = 1;
        }

        if (cropHeight < 1)
        {
            cropHeight = 1;
        }

        if (start >= width)
        {
            start = width - 1;
        }

        if (top >= height)
        {
            top = height - 1;
        }

        if (start + cropWidth > width)
        {
            cropWidth = width - start;
        }

        if (top + cropHeight > height)
        {
            cropHeight = height - top;
        }

        return new Rectangle(start, top, cropWidth, cropHeight);
    }
}
