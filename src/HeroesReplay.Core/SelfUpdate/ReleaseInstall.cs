using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Collection;

namespace HeroesReplay.Core.SelfUpdate;

public static class ReleaseInstall
{
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
    /// What <c>apply-release.ps1</c> does with the release's OBS files. The scene collection goes
    /// through <see cref="ObsCollectionPatcher"/> as a release: a collection HeroesReplay manages is
    /// replaced with this install's template (backed up first), a custom one is kept and reported,
    /// and while OBS is running the replacement waits until HeroesReplay finds OBS closed. The
    /// profile (<c>basic.ini</c>) is machine-owned: the packaged one is only a template, written when
    /// this machine has no profile of that name and OBS is closed. <c>service.json</c> (the stream
    /// key) is never copied. Returns one line per decision for the update log. First the install's
    /// <c>obs</c> folder is checked against <c>obs/bundle.manifest</c>
    /// (<see cref="ObsCollectionBundle"/>).
    /// </summary>
    /// <exception cref="ObsBundleInvalidException">
    /// A packaged OBS file does not match the manifest. Nothing was written.
    /// </exception>
    public static IReadOnlyList<string> InstallObsFiles(ReleaseObsInstall request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var notes = new List<string>();
        if (
            string.IsNullOrWhiteSpace(request.InstallDirectory)
            || string.IsNullOrWhiteSpace(request.AppData)
        )
        {
            return notes;
        }

        // The staged OBS files must be the ones the release packaged before anything is written.
        // A folder with no manifest (a release packaged before schema 2) installs with a warning.
        string obsDirectory = Path.Combine(request.InstallDirectory, "obs");
        if (Directory.Exists(obsDirectory))
        {
            ObsBundleCheck bundle = ObsCollectionBundle.Verify(obsDirectory);
            if (!bundle.Ok)
            {
                throw new ObsBundleInvalidException(bundle);
            }

            notes.Add("OBS bundle: " + bundle.Describe());
        }

        string scene = Path.Combine(obsDirectory, "Default.json");
        if (File.Exists(scene))
        {
            ObsCollectionApplyResult collection = ObsCollectionPatcher.Apply(
                new ObsCollectionUpdate
                {
                    TemplatePath = scene,
                    DestinationPath = ObsNames.CollectionFile(
                        request.AppData,
                        request.CollectionName
                    ),
                    DataDirectory = request.DataDirectory,
                    ObsIsRunning = request.ObsIsRunning,
                    CollectionName = request.CollectionName,
                    Managed = request.Managed,
                    Release = true,
                    PreviousTemplatePath = string.IsNullOrWhiteSpace(request.PreviousInstall)
                        ? null
                        : Path.Combine(request.PreviousInstall, "obs", "Default.json"),
                    UtcNow = request.UtcNow,
                }
            );
            notes.Add("OBS collection: " + collection.Message);
        }

        string ini = Path.Combine(request.InstallDirectory, "obs", "Default", "basic.ini");
        if (File.Exists(ini))
        {
            string profile = ObsNames.ProfileIni(request.AppData, request.ProfileName);
            if (File.Exists(profile))
            {
                notes.Add(
                    "Kept the existing OBS profile "
                        + profile
                        + ". The profile belongs to this machine; a release does not replace it."
                );
            }
            else if (request.ObsIsRunning)
            {
                notes.Add(
                    "OBS is running, so the profile template was not written to " + profile + "."
                );
            }
            else
            {
                ObsFileTransaction.Write(
                    profile,
                    ObsNames.WithProfileName(File.ReadAllText(ini), request.ProfileName),
                    request.Managed.BackupDirectory,
                    request.UtcNow
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

/// <summary>The OBS part of a release install (<see cref="ReleaseInstall.InstallObsFiles"/>).</summary>
public sealed record ReleaseObsInstall
{
    /// <summary>The new install, with <c>obs\Default.json</c> and <c>obs\Default\basic.ini</c>.</summary>
    public string InstallDirectory { get; init; }

    /// <summary><c>%APPDATA%</c>, which holds <c>obs-studio</c>.</summary>
    public string AppData { get; init; }

    public bool ObsIsRunning { get; init; }

    public ObsManagedFiles Managed { get; init; }

    /// <summary>The effective <c>Location:DataDirectory</c> of the new install.</summary>
    public string DataDirectory { get; init; }

    public string ProfileName { get; init; }

    public string CollectionName { get; init; }

    /// <summary>The install being replaced (<c>app.previous</c>), when there is one.</summary>
    public string PreviousInstall { get; init; }

    public DateTime UtcNow { get; init; } = DateTime.UtcNow;
}
