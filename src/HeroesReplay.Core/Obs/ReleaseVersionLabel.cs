using System;
using System.IO;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Obs;

/// <summary>
/// The HeroesReplay release this install runs (the GitHub release tag the update downloaded, from
/// version.txt), shown by the <c>release-version</c> text source in the bottom-right corner
/// of the waiting scene. Spectate writes <c>Data\heroesreplay-version.txt</c> when it starts and OBS reads
/// the file ("Read from file"), so the waiting screen shows a new release as soon as it runs.
/// </summary>
public static class ReleaseVersionLabel
{
    public const string FileName = "heroesreplay-version.txt";

    /// <summary>The release tag from version.txt, or <c>dev &lt;commit&gt;</c> for a source build.</summary>
    public static string Text(string releaseVersion, string informationalVersion)
    {
        if (!string.IsNullOrWhiteSpace(releaseVersion))
        {
            return releaseVersion.Trim();
        }

        int plus = informationalVersion?.IndexOf('+') ?? -1;
        string commit = plus >= 0 ? informationalVersion[(plus + 1)..].Trim() : string.Empty;
        return commit.Length == 0 ? "dev" : "dev " + commit[..Math.Min(7, commit.Length)];
    }

    /// <summary>Writes this install's label into <paramref name="dataDirectory"/>. Returns the text.</summary>
    public static string WriteForThisInstall(string dataDirectory)
    {
        string text = Text(
            ReleaseInstall.ReadVersion(Path.GetDirectoryName(Environment.ProcessPath)),
            ServiceReadyFile.CurrentVersion()
        );
        if (!string.IsNullOrWhiteSpace(dataDirectory))
        {
            // Replaced, not rewritten in place: OBS may be reading the file.
            DurableFile.Replace(Path.Combine(dataDirectory, FileName), text);
        }

        return text;
    }
}
