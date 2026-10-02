using System.Drawing;

namespace HeroesReplay.Core.Spectating.Clock.Ocr;

/// <summary>
/// Client-pixel crop of the observer score-well clock.
/// Measured on the 1280x720 HUD: the MM:SS glyphs occupy x=930..994, y=25..41.
/// The fort scores sit just outside that, at about x=884..892 and x=1030..1038.
/// A centered crop at this size lands on the hero portraits.
/// </summary>
public static class HudTimerCrop
{
    public static Rectangle ForClient(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return Rectangle.Empty;
        }

        int start = width * 900 / 1280;
        int top = height * 18 / 720;
        int cropWidth = width * 120 / 1280;
        int cropHeight = height * 34 / 720;
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
