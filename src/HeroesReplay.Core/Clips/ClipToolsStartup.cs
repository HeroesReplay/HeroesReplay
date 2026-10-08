using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Clips;

/// <summary>
/// Spectate's start: with clips on (<c>OBS:RecordingEnabled</c>), a missing ffmpeg or ffprobe is
/// one error now instead of a warning after a pentakill.
/// </summary>
public static class ClipToolsStartup
{
    /// <summary>The tools clips need that cannot be found. Empty when clips are off.</summary>
    public static IReadOnlyList<FfmpegResolution> Missing(
        AppSettings settings,
        FfmpegLocator locator
    )
    {
        if (settings?.OBS?.RecordingEnabled != true)
        {
            return [];
        }

        return FfmpegLocator
            .Tools.Select(locator.Resolve)
            .Where(resolution => !resolution.Found)
            .ToList();
    }

    /// <summary>Logs one error when a tool is missing. Returns false then.</summary>
    public static bool Check(AppSettings settings, ILogger logger, FfmpegLocator locator = null)
    {
        locator ??= FfmpegLocator.From(settings?.Clips, settings?.Dependencies);
        IReadOnlyList<FfmpegResolution> missing = Missing(settings, locator);
        if (missing.Count == 0)
        {
            return true;
        }

        logger?.LogError(
            "Clips are on (OBS:RecordingEnabled) but {Tools} could not be found in {Searched}, so no pentakill clip will be cut. Run `heroesreplay deps install`, then `heroesreplay check ffmpeg`.",
            string.Join(" and ", missing.Select(tool => tool.Tool + ".exe")),
            locator.DescribeSearch()
        );
        return false;
    }
}
