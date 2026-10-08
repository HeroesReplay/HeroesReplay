using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Dependencies;

namespace HeroesReplay.Core.Clips;

/// <summary>Where a tool was found. <see cref="Missing"/> means nowhere.</summary>
public enum FfmpegSource
{
    Missing,
    Configured,
    Installed,
    Legacy,
    Path,
}

public sealed record FfmpegResolution(string Tool, string Path, FfmpegSource Source)
{
    public bool Found => Source != FfmpegSource.Missing;
}

/// <summary>
/// Finds <c>ffmpeg.exe</c> and <c>ffprobe.exe</c>, first match wins: <c>Clips:FfmpegDirectory</c>,
/// then the <c>deps install</c> folder (<c>Dependencies:Directory\ffmpeg</c>, by default
/// <c>C:\heroesreplay\tools\ffmpeg</c>), then <c>C:\ffmpeg\bin</c>, then PATH.
/// </summary>
public sealed class FfmpegLocator
{
    public const string Ffmpeg = "ffmpeg";
    public const string Ffprobe = "ffprobe";
    public const string LegacyDirectory = @"C:\ffmpeg\bin";

    public static readonly IReadOnlyList<string> Tools = new[] { Ffmpeg, Ffprobe };

    private readonly Func<string, bool> fileExists;
    private readonly string pathVariable;

    public FfmpegLocator(
        string configuredDirectory,
        string installDirectory,
        string pathVariable = null,
        Func<string, bool> fileExists = null
    )
    {
        ConfiguredDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? null
            : configuredDirectory.Trim();
        InstallDirectory = string.IsNullOrWhiteSpace(installDirectory)
            ? null
            : installDirectory.Trim();
        this.pathVariable = pathVariable ?? Environment.GetEnvironmentVariable("PATH");
        this.fileExists = fileExists ?? File.Exists;
    }

    /// <summary>The locator for this install's <c>Clips</c> and <c>Dependencies</c> settings.</summary>
    public static FfmpegLocator From(ClipSettings clips, DependencySettings dependencies) =>
        new(
            clips?.FfmpegDirectory,
            DependencySettings.ToolDirectory(dependencies, DependencyManifest.FfmpegName)
        );

    public string ConfiguredDirectory { get; }

    public string InstallDirectory { get; }

    public FfmpegResolution Resolve(string tool)
    {
        string file = tool + ".exe";
        foreach ((string directory, FfmpegSource source) in Directories())
        {
            string candidate = Combine(directory, file);
            if (candidate != null && fileExists(candidate))
            {
                return new FfmpegResolution(tool, candidate, source);
            }
        }

        foreach (string directory in (pathVariable ?? string.Empty).Split(';'))
        {
            string candidate = Combine(directory.Trim().Trim('"'), file);
            if (candidate != null && fileExists(candidate))
            {
                return new FfmpegResolution(tool, candidate, FfmpegSource.Path);
            }
        }

        return new FfmpegResolution(tool, null, FfmpegSource.Missing);
    }

    /// <summary>The full path, or the bare tool name so a process start still tries PATH.</summary>
    public string Find(string tool) => Resolve(tool).Path ?? tool;

    /// <summary>Where <see cref="Resolve"/> looks, in order, for a message about a missing tool.</summary>
    public string DescribeSearch()
    {
        var places = new List<string>();
        foreach ((string directory, FfmpegSource source) in Directories())
        {
            places.Add(
                source == FfmpegSource.Configured ? $"Clips:FfmpegDirectory {directory}" : directory
            );
        }

        places.Add("PATH");
        return string.Join(", ", places);
    }

    private IEnumerable<(string Directory, FfmpegSource Source)> Directories()
    {
        if (ConfiguredDirectory != null)
        {
            yield return (ConfiguredDirectory, FfmpegSource.Configured);
        }

        if (InstallDirectory != null)
        {
            yield return (InstallDirectory, FfmpegSource.Installed);
        }

        yield return (LegacyDirectory, FfmpegSource.Legacy);
    }

    private static string Combine(string directory, string file)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            return System.IO.Path.Combine(directory, file);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
