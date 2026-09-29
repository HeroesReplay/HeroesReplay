using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HeroesReplay.Core.Services.Observer;

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
}
