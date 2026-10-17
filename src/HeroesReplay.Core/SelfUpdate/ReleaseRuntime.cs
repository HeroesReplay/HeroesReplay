using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HeroesReplay.Core.SelfUpdate;

/// <summary>
/// The .NET runtime a release was built and tested on (#462). <c>tools/package-release.ps1</c>
/// asks the packaged exe (<c>check runtime</c>) and writes its
/// <see cref="RuntimeInformation.FrameworkDescription"/>, such as <c>.NET 10.0.13</c>, into
/// <see cref="FileName"/> beside version.txt, and <c>release.yml</c> names it in the release notes.
/// version.txt stays the tag alone: the update compares it to the release tag. A
/// framework-dependent install runs on the machine-wide .NET runtime that Microsoft Update
/// services. A self-contained or Native AOT release carries this one, so a .NET security fix
/// reaches it only with a new release (<c>docs/runtime-currency.md</c>).
/// </summary>
public static class ReleaseRuntime
{
    public const string FileName = "runtime.txt";

    /// <summary>The warning reason of <c>check runtime</c>, after <c>check.runtime.</c>.</summary>
    public const string OlderThanRelease = "older_than_release";

    private static readonly Regex VersionPattern = new(
        @"(\d+)\.(\d+)\.(\d+)",
        RegexOptions.CultureInvariant
    );

    /// <summary>The runtime this process runs on, such as <c>.NET 10.0.13</c>.</summary>
    public static string Current => RuntimeInformation.FrameworkDescription;

    /// <summary>
    /// The runtime the release in <paramref name="installDirectory"/> was built and tested on, or
    /// null when it has no runtime.txt: a source build, or a release from before #462.
    /// </summary>
    public static string Read(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return null;
        }

        string path = Path.Combine(installDirectory, FileName);
        string text = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        return text.Length == 0 ? null : text;
    }

    /// <summary>The version in a framework description: 10.0.13 from <c>.NET 10.0.13</c>. Null when there is none.</summary>
    public static Version ParseVersion(string description)
    {
        Match match = VersionPattern.Match(description ?? "");
        return match.Success
            ? new Version(
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                int.Parse(match.Groups[3].Value)
            )
            : null;
    }

    /// <summary>
    /// What <c>check runtime</c> reports. The detail always starts with <paramref name="current"/>,
    /// which <c>package-release.ps1</c> reads from the JSON. A framework-dependent install on a
    /// runtime older than its release's means Microsoft Update has not serviced this machine yet.
    /// </summary>
    public static ReleaseRuntimeReport Evaluate(string current, string folder, string release)
    {
        string from = string.IsNullOrWhiteSpace(folder)
            ? ""
            : $", from {folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}";
        if (string.IsNullOrWhiteSpace(release))
        {
            return new ReleaseRuntimeReport(
                current,
                null,
                false,
                $"{current}{from}. No {FileName} beside the exe: a source build, or a release from before #462."
            );
        }

        Version running = ParseVersion(current);
        Version built = ParseVersion(release);
        bool older = running != null && built != null && running < built;
        return new ReleaseRuntimeReport(
            current,
            release,
            older,
            older
                ? $"{current}{from}, is older than {release}, the runtime this release was built and tested on ({FileName}). Microsoft Update has not serviced this machine's .NET runtime yet."
                : $"{current}{from}. This release was built and tested on {release} ({FileName})."
        );
    }
}

/// <summary>
/// <see cref="ReleaseRuntime.Evaluate"/>: the runtime this exe runs on, the one its release names
/// (null without runtime.txt), and whether this one is older.
/// </summary>
public sealed record ReleaseRuntimeReport(
    string Runtime,
    string Release,
    bool OlderThanRelease,
    string Detail
);
