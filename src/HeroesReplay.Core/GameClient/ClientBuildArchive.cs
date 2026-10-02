using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.HeroesProfile;

namespace HeroesReplay.Core.GameClient;

public enum ClientBuildKeep
{
    Copied,
    AlreadyKept,
    Skipped,
}

public readonly record struct ClientBuildKeepItem(string Version, ClientBuildKeep Result);

/// <summary>
/// Battle.net reclaim deletes the previous 2.57 build's exe from Versions.
/// A copy under Data\Clients is what lets that iteration launch later.
/// The copy is not a substitute client: HeroesSwitcher still needs the exe back in Versions\Base*.
/// </summary>
public static class ClientBuildArchive
{
    public static string ResolveDirectory(string configured, string dataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return null;
        }

        return Path.Combine(dataDirectory.Trim(), "Clients");
    }

    public static string BaseDirectoryName(string version)
    {
        string normalized = ReplayClientRoute.Normalize(version);
        if (normalized.Length == 0)
        {
            return null;
        }

        string[] parts = normalized.Split('.');
        string build = parts[parts.Length - 1];
        if (build.Length == 0 || build.Length > 8)
        {
            return null;
        }

        foreach (char character in build)
        {
            if (character < '0' || character > '9')
            {
                return null;
            }
        }

        return "Base" + build;
    }

    /// <summary>
    /// Keep every installed build on the configured patch line, and any newer installed build.
    /// An empty minimum keeps every installed exe.
    /// </summary>
    public static bool ShouldKeep(string version, string minimumVersion)
    {
        string candidate = ReplayClientRoute.Normalize(version);
        if (candidate.Length == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(minimumVersion))
        {
            return true;
        }

        string floor = ReplayClientRoute.Normalize(minimumVersion);
        return GameVersionOrder.SamePatch(candidate, floor)
            || GameVersionOrder.IsAtLeast(candidate, floor);
    }

    public static IReadOnlyList<ClientBuildKeepItem> Preserve(
        IReadOnlyList<InstalledClient> installed,
        string archiveDirectory,
        string minimumVersion
    )
    {
        var kept = new List<ClientBuildKeepItem>();
        if (installed == null || string.IsNullOrWhiteSpace(archiveDirectory))
        {
            return kept;
        }

        foreach (InstalledClient client in installed)
        {
            string version = ReplayClientRoute.Normalize(client.Version);
            if (!ShouldKeep(version, minimumVersion))
            {
                continue;
            }

            string folder = BaseDirectoryName(version);
            if (folder == null || string.IsNullOrWhiteSpace(client.ExePath))
            {
                kept.Add(new ClientBuildKeepItem(version, ClientBuildKeep.Skipped));
                continue;
            }

            string destination = Path.Combine(
                archiveDirectory,
                folder,
                InstalledClientCatalog.ExeFileName
            );
            kept.Add(new ClientBuildKeepItem(version, CopyExe(client.ExePath, destination)));
        }

        return kept;
    }

    public static bool Restore(
        string gameInstallDirectory,
        string archiveDirectory,
        string replayVersion
    )
    {
        if (
            string.IsNullOrWhiteSpace(gameInstallDirectory)
            || string.IsNullOrWhiteSpace(archiveDirectory)
        )
        {
            return false;
        }

        string folder = BaseDirectoryName(replayVersion);
        if (folder == null)
        {
            return false;
        }

        string live = Path.Combine(
            gameInstallDirectory,
            "Versions",
            folder,
            InstalledClientCatalog.ExeFileName
        );
        if (File.Exists(live))
        {
            return false;
        }

        string archived = Path.Combine(
            archiveDirectory,
            folder,
            InstalledClientCatalog.ExeFileName
        );
        if (!File.Exists(archived))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(live));
            File.Copy(archived, live, overwrite: false);
            return File.Exists(live);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ClientBuildKeep CopyExe(string source, string destination)
    {
        try
        {
            var sourceInfo = new FileInfo(source);
            if (!sourceInfo.Exists || sourceInfo.Length <= 0)
            {
                return ClientBuildKeep.Skipped;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (File.Exists(destination))
            {
                var kept = new FileInfo(destination);
                if (kept.Length > 0)
                {
                    return ClientBuildKeep.AlreadyKept;
                }
            }

            string temporary = destination + ".part";
            File.Copy(source, temporary, overwrite: true);
            var copied = new FileInfo(temporary);
            if (copied.Length != sourceInfo.Length)
            {
                File.Delete(temporary);
                return ClientBuildKeep.Skipped;
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(temporary, destination);
            return ClientBuildKeep.Copied;
        }
        catch (IOException)
        {
            return ClientBuildKeep.Skipped;
        }
        catch (UnauthorizedAccessException)
        {
            return ClientBuildKeep.Skipped;
        }
        catch (ArgumentException)
        {
            return ClientBuildKeep.Skipped;
        }
    }
}
