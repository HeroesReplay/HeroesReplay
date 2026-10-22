using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace HeroesReplay.Core.Spectating.Capture;

/// <summary>
/// The end-of-game capture in a replay's context folder: one <see cref="FileName"/> (#491). It
/// was written as <c>end-HH-MM-SS.png</c> and then copied to <c>end.png</c>, byte for byte: 940
/// PNGs and 1.1 GB of duplicates on the stream PC. Nothing in HeroesReplay reads either name; an
/// agent reads <c>end.png</c> after a proof (AGENTS.md), and the match clock it was taken at is in
/// the log line.
/// </summary>
public static class EndScreenshot
{
    public const string FileName = "end.png";

    /// <summary>Saves <paramref name="bitmap"/> as the folder's <see cref="FileName"/> and returns its path.</summary>
    public static string Save(Bitmap bitmap, string directory)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
