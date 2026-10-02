using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Obs;

namespace HeroesReplay.Core.SelfUpdate;

public static class ReleaseInstall
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string ReadVersion(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return "";
        }

        string path = Path.Combine(installDirectory, ReleaseSettingsFileName());
        if (!File.Exists(path))
        {
            return "";
        }

        return File.ReadAllText(path).Trim();
    }

    public static bool LooksLikeSourceBuild(string installDirectory)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return true;
        }

        string full = Path.GetFullPath(installDirectory);
        return full.Contains(
                $"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase
            )
            || full.Contains(
                $"{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase
            );
    }

    public static string FindPublishRoot(string extractedDirectory)
    {
        if (string.IsNullOrWhiteSpace(extractedDirectory) || !Directory.Exists(extractedDirectory))
        {
            return null;
        }

        if (File.Exists(Path.Combine(extractedDirectory, "heroesreplay.exe")))
        {
            return extractedDirectory;
        }

        foreach (
            string file in Directory.EnumerateFiles(
                extractedDirectory,
                "heroesreplay.exe",
                SearchOption.AllDirectories
            )
        )
        {
            return Path.GetDirectoryName(file);
        }

        return null;
    }

    public static void CopyPublish(string publishRoot, string destination)
    {
        if (string.IsNullOrWhiteSpace(publishRoot) || string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("Publish root and destination are required.");
        }

        if (!File.Exists(Path.Combine(publishRoot, "heroesreplay.exe")))
        {
            throw new InvalidOperationException($"No heroesreplay.exe in {publishRoot}.");
        }

        Directory.CreateDirectory(destination);
        foreach (
            string file in Directory.EnumerateFiles(publishRoot, "*", SearchOption.AllDirectories)
        )
        {
            if (IsStreamKey(file))
            {
                continue;
            }

            string relative = Path.GetRelativePath(publishRoot, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    public static void PreserveMinReplayId(string previousSettingsPath, string targetSettingsPath)
    {
        if (
            string.IsNullOrWhiteSpace(previousSettingsPath)
            || string.IsNullOrWhiteSpace(targetSettingsPath)
        )
        {
            return;
        }

        if (!File.Exists(previousSettingsPath) || !File.Exists(targetSettingsPath))
        {
            return;
        }

        string previous = File.ReadAllText(previousSettingsPath);
        string incoming = File.ReadAllText(targetSettingsPath);
        if (!MinReplayIdFile.TryPreserveHigher(incoming, previous, out string updated))
        {
            return;
        }

        string temp = targetSettingsPath + ".tmp";
        File.WriteAllText(temp, updated);
        try
        {
            File.Move(temp, targetSettingsPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            throw;
        }
    }

    public static void PreserveSecrets(
        string secretsPath,
        string previousInstall,
        string destination
    )
    {
        string target = Path.Combine(destination, "appsettings.secrets.json");
        if (!string.IsNullOrWhiteSpace(secretsPath) && File.Exists(secretsPath))
        {
            File.Copy(secretsPath, target, overwrite: true);
            return;
        }

        string previous = Path.Combine(previousInstall ?? "", "appsettings.secrets.json");
        if (File.Exists(previous))
        {
            File.Copy(previous, target, overwrite: true);
        }
    }

    /// <summary>
    /// What <c>apply-release.ps1</c> does with the release's OBS files. While OBS is closed the
    /// scene collection is replaced, as before, and <c>services start</c> then points its paths at
    /// this install. The profile (<c>basic.ini</c>) is machine-owned: the packaged one is only a
    /// template, copied when this machine has no profile of that name. <c>service.json</c> (the
    /// stream key) is never copied. Returns one line per decision for the update log.
    /// </summary>
    public static IReadOnlyList<string> CopyObsScenesIfClosed(
        string installDirectory,
        string appData,
        bool obsIsRunning,
        string profileName = null,
        string collectionName = null
    )
    {
        var notes = new List<string>();
        if (string.IsNullOrWhiteSpace(installDirectory) || string.IsNullOrWhiteSpace(appData))
        {
            return notes;
        }

        if (obsIsRunning)
        {
            notes.Add(
                "OBS is open. Scene files in the release were left under obs\\ and were not copied."
            );
            return notes;
        }

        string scene = Path.Combine(installDirectory, "obs", "Default.json");
        if (File.Exists(scene))
        {
            string destination = ObsNames.CollectionFile(appData, collectionName);
            string json = File.ReadAllText(scene);
            if (!string.IsNullOrWhiteSpace(collectionName))
            {
                json = ObsNames.WithCollectionName(json, collectionName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, json, Utf8);
            notes.Add("OBS collection -> " + destination);
        }

        string ini = Path.Combine(installDirectory, "obs", "Default", "basic.ini");
        if (File.Exists(ini))
        {
            string profile = ObsNames.ProfileIni(appData, profileName);
            if (File.Exists(profile))
            {
                notes.Add(
                    "Kept the existing OBS profile "
                        + profile
                        + ". The profile belongs to this machine; a release does not replace it."
                );
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
                File.WriteAllText(
                    profile,
                    ObsNames.WithProfileName(File.ReadAllText(ini), profileName),
                    Utf8
                );
                notes.Add(
                    "OBS profile -> "
                        + profile
                        + " (template; this machine had none). Set its encoder, bitrate, and stream service in OBS."
                );
            }
        }

        return notes;
    }

    private static string ReleaseSettingsFileName() => "version.txt";

    private static bool IsStreamKey(string file)
    {
        return string.Equals(
            Path.GetFileName(file),
            "service.json",
            StringComparison.OrdinalIgnoreCase
        );
    }
}
