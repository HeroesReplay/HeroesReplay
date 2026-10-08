using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Clips;

/// <summary>
/// What one resolved tool reported. <see cref="VersionLine"/> is the first <c>-version</c> line,
/// <see cref="EncodesH264"/> whether <c>ffmpeg -encoders</c> lists libx264 (null for ffprobe).
/// </summary>
public sealed record FfmpegToolStatus(
    FfmpegResolution Resolution,
    string VersionLine,
    bool? EncodesH264,
    string Error
);

public sealed record FfmpegCheckReport(bool Ok, bool Warning, string Detail);

/// <summary>
/// <c>heroesreplay check ffmpeg</c>. Fails when ffmpeg or ffprobe is missing, does not report a
/// version, or (ffmpeg) cannot encode libx264, which <see cref="FfmpegArguments.Cut"/> uses.
/// A tool that works but is not the pinned version passes with a warning: clips still cut, and
/// <c>deps install</c> puts the pinned build in a folder searched before <c>C:\ffmpeg\bin</c> and PATH.
/// </summary>
public static class FfmpegCheck
{
    public const string DepsInstallHint = "Run `heroesreplay deps install`.";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    public static async Task<FfmpegToolStatus> ProbeAsync(FfmpegResolution resolution)
    {
        if (!resolution.Found)
        {
            return new FfmpegToolStatus(resolution, null, null, null);
        }

        (string version, string versionError) = await RunAsync(resolution.Path, "-version")
            .ConfigureAwait(false);
        string line = FirstLine(version);
        if (line == null)
        {
            return new FfmpegToolStatus(resolution, null, null, versionError ?? "no output");
        }

        bool? encodes = null;
        if (resolution.Tool == FfmpegLocator.Ffmpeg)
        {
            (string encoders, _) = await RunAsync(resolution.Path, "-hide_banner -encoders")
                .ConfigureAwait(false);
            encodes = ListsEncoder(encoders, "libx264");
        }

        return new FfmpegToolStatus(resolution, line, encodes, null);
    }

    public static FfmpegCheckReport Evaluate(
        string pinnedVersion,
        IReadOnlyList<FfmpegToolStatus> tools,
        string searched
    )
    {
        bool ok = true;
        bool warning = false;
        var lines = new List<string>();
        foreach (FfmpegToolStatus tool in tools)
        {
            FfmpegResolution found = tool.Resolution;
            if (!found.Found)
            {
                ok = false;
                lines.Add($"{found.Tool}: not found in {searched}. {DepsInstallHint}");
                continue;
            }

            string where = $"{found.Path} ({Describe(found.Source)})";
            if (tool.VersionLine == null)
            {
                ok = false;
                lines.Add(
                    $"{found.Tool}: {where} did not report a version ({tool.Error}). {DepsInstallHint}"
                );
                continue;
            }

            if (tool.EncodesH264 == false)
            {
                ok = false;
                lines.Add(
                    $"{found.Tool}: {where}: {tool.VersionLine}. It cannot encode libx264, which clips use. {DepsInstallHint}"
                );
                continue;
            }

            string version = ParseVersion(tool.VersionLine, found.Tool);
            if (!IsPinned(version, pinnedVersion))
            {
                warning = true;
                lines.Add(
                    $"{found.Tool}: {where}: {tool.VersionLine}. Not the pinned {pinnedVersion}; clips still cut. {DepsInstallHint}"
                );
                continue;
            }

            lines.Add($"{found.Tool}: {where}: {tool.VersionLine}");
        }

        string summary =
            !ok ? "Clips cannot be cut."
            : warning ? $"Not every tool is the pinned {pinnedVersion}; clips still cut."
            : $"The pinned {pinnedVersion}.";
        return new FfmpegCheckReport(
            ok,
            ok && warning,
            summary + string.Concat(lines.Select(line => Environment.NewLine + "  " + line))
        );
    }

    /// <summary>
    /// The version word of a <c>-version</c> line: <c>9.0.2-essentials_build-www.gyan.dev</c> from
    /// <c>ffmpeg version 9.0.2-essentials_build-www.gyan.dev Copyright ...</c>. Null when the line
    /// is not one.
    /// </summary>
    public static string ParseVersion(string line, string tool)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 2 < words.Length; i++)
        {
            if (
                string.Equals(words[i], tool, StringComparison.OrdinalIgnoreCase)
                && string.Equals(words[i + 1], "version", StringComparison.OrdinalIgnoreCase)
            )
            {
                return words[i + 2];
            }
        }

        return null;
    }

    /// <summary>
    /// <c>9.0.2</c>, <c>9.0.2-essentials_build-www.gyan.dev</c> and <c>n9.0.2-...</c> are the pinned
    /// 9.0.2; <c>9.0.21</c> and <c>9.0</c> are not.
    /// </summary>
    public static bool IsPinned(string version, string pinned)
    {
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(pinned))
        {
            return false;
        }

        if (version.Length > 1 && version[0] == 'n' && char.IsDigit(version[1]))
        {
            version = version.Substring(1);
        }

        return string.Equals(version, pinned, StringComparison.OrdinalIgnoreCase)
            || version.StartsWith(pinned + "-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ListsEncoder(string encoders, string name) =>
        !string.IsNullOrWhiteSpace(encoders)
        && encoders
            .Split('\n')
            .Any(line =>
                line.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, string encoder, ..]
                && string.Equals(encoder, name, StringComparison.Ordinal)
            );

    private static string Describe(FfmpegSource source) =>
        source switch
        {
            FfmpegSource.Configured => "Clips:FfmpegDirectory",
            FfmpegSource.Installed => "deps install",
            FfmpegSource.Legacy => "C:\\ffmpeg\\bin",
            FfmpegSource.Path => "PATH",
            _ => "missing",
        };

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string line = text.Split('\n')[0].Trim();
        int copyright = line.IndexOf(" Copyright", StringComparison.Ordinal);
        return copyright > 0 ? line.Substring(0, copyright) : line;
    }

    private static async Task<(string Output, string Error)> RunAsync(string path, string arguments)
    {
        try
        {
            using Process process = Process.Start(
                new ProcessStartInfo(path, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            );
            if (process == null)
            {
                return (null, "did not start");
            }

            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            Task exited = process.WaitForExitAsync();
            if (
                await Task.WhenAny(exited, Task.Delay(ProbeTimeout)).ConfigureAwait(false) != exited
            )
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }

                return (null, $"no exit within {ProbeTimeout.TotalSeconds:0} s");
            }

            string text = await output.ConfigureAwait(false);
            string stderr = await error.ConfigureAwait(false);
            return process.ExitCode == 0
                ? (text, null)
                : (null, $"exit {process.ExitCode}: {FirstLine(stderr) ?? "no error text"}");
        }
        catch (Exception e) when (e is Win32Exception or IOException)
        {
            return (null, e.Message);
        }
    }
}
