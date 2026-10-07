using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HeroesReplay.Core.GameClient;

public readonly record struct InstalledClient(string Version, string ExePath);

/// <summary>
/// Reads file versions from Versions\Base*\HeroesOfTheStorm_x64.exe.
/// A folder with no executable is not an installed client.
/// </summary>
public static class InstalledClientCatalog
{
    public const string ExeFileName = "HeroesOfTheStorm_x64.exe";

    public static IReadOnlyList<string> FileVersions(string gameInstallDirectory)
    {
        var versions = new List<string>();
        foreach (InstalledClient client in Clients(gameInstallDirectory))
        {
            versions.Add(client.Version);
        }

        return versions;
    }

    public static IReadOnlyList<InstalledClient> Clients(string gameInstallDirectory)
    {
        var clients = new List<InstalledClient>();
        if (string.IsNullOrWhiteSpace(gameInstallDirectory))
        {
            return clients;
        }

        string root = Path.Combine(gameInstallDirectory, "Versions");
        if (!Directory.Exists(root))
        {
            return clients;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (IOException)
        {
            return clients;
        }
        catch (UnauthorizedAccessException)
        {
            return clients;
        }

        foreach (string directory in directories)
        {
            string exe = Path.Combine(directory, ExeFileName);
            if (!File.Exists(exe))
            {
                continue;
            }

            try
            {
                string version = FileVersionInfo.GetVersionInfo(exe).FileVersion;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    clients.Add(new InstalledClient(version.Trim(), exe));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return clients;
    }

    /// <summary>
    /// Where the exe for <paramref name="version"/> lives: <c>Versions\Base&lt;build&gt;</c>.
    /// Null when the build number cannot be read. The file may not exist.
    /// </summary>
    public static string ExePathFor(string gameInstallDirectory, string version)
    {
        string folder = ClientBuildArchive.BaseDirectoryName(version);
        if (folder == null || string.IsNullOrWhiteSpace(gameInstallDirectory))
        {
            return null;
        }

        return Path.Combine(gameInstallDirectory, "Versions", folder, ExeFileName);
    }

    public static string FindExe(IEnumerable<InstalledClient> clients, string version)
    {
        if (clients == null || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        foreach (InstalledClient client in clients)
        {
            if (ReplayClientRoute.SameBuild(client.Version, version))
            {
                return client.ExePath;
            }
        }

        return null;
    }
}
