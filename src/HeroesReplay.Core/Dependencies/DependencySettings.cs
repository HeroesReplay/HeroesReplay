using System;
using System.IO;

namespace HeroesReplay.Core.Dependencies;

/// <summary>
/// <c>Dependencies</c>: where <c>heroesreplay deps install</c> puts the pinned tools. Each tool
/// gets its own folder, <c>Directory\&lt;name&gt;</c> (ffmpeg: <c>C:\heroesreplay\tools\ffmpeg</c>).
/// </summary>
public sealed class DependencySettings
{
    public const string DefaultDirectory = @"C:\heroesreplay\tools";

    public string Directory { get; set; } = DefaultDirectory;

    /// <summary>The whole install of one tool, download included, gives up after this.</summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(5);

    public static string Root(DependencySettings settings, string overrideDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.GetFullPath(overrideDirectory.Trim());
        }

        return string.IsNullOrWhiteSpace(settings?.Directory)
            ? DefaultDirectory
            : settings.Directory.Trim();
    }

    public static string ToolDirectory(DependencySettings settings, string name) =>
        Path.Combine(Root(settings), name);
}
