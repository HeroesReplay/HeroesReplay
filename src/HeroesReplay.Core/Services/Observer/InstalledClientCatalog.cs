using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Reads file versions from Versions\Base*\HeroesOfTheStorm_x64.exe.
/// A folder with no executable is not an installed client.
/// </summary>
public static class InstalledClientCatalog
{
    public static IReadOnlyList<string> FileVersions(string gameInstallDirectory)
    {
        var versions = new List<string>();
        if (string.IsNullOrWhiteSpace(gameInstallDirectory))
        {
            return versions;
        }

        string root = Path.Combine(gameInstallDirectory, "Versions");
        if (!Directory.Exists(root))
        {
            return versions;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (IOException)
        {
            return versions;
        }
        catch (UnauthorizedAccessException)
        {
            return versions;
        }

        foreach (string directory in directories)
        {
            string exe = Path.Combine(directory, "HeroesOfTheStorm_x64.exe");
            if (!File.Exists(exe))
            {
                continue;
            }

            try
            {
                string version = FileVersionInfo.GetVersionInfo(exe).FileVersion;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    versions.Add(version.Trim());
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return versions;
    }
}
