using System;
using System.IO;

namespace HeroesReplay.Core.Services.SelfUpdate;

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

        foreach (string file in Directory.EnumerateFiles(extractedDirectory, "heroesreplay.exe", SearchOption.AllDirectories))
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
        foreach (string file in Directory.EnumerateFiles(publishRoot, "*", SearchOption.AllDirectories))
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

    public static void PreserveSecrets(string secretsPath, string previousInstall, string destination)
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

    public static void Swap(string installDirectory, string stagedDirectory)
    {
        if (LooksLikeSourceBuild(installDirectory))
        {
            throw new InvalidOperationException($"Refusing to replace a source build at {installDirectory}.");
        }

        string previous = installDirectory.TrimEnd(Path.DirectorySeparatorChar) + ".previous";
        if (Directory.Exists(previous))
        {
            Directory.Delete(previous, recursive: true);
        }

        Directory.Move(installDirectory, previous);
        try
        {
            Directory.Move(stagedDirectory, installDirectory);
        }
        catch
        {
            if (!Directory.Exists(installDirectory) && Directory.Exists(previous))
            {
                Directory.Move(previous, installDirectory);
            }

            throw;
        }
    }

    public static void CopyObsScenesIfClosed(string installDirectory, string appData, bool obsIsRunning)
    {
        if (obsIsRunning || string.IsNullOrWhiteSpace(installDirectory) || string.IsNullOrWhiteSpace(appData))
        {
            return;
        }

        string scene = Path.Combine(installDirectory, "obs", "Default.json");
        if (File.Exists(scene))
        {
            string destination = Path.Combine(appData, "obs-studio", "basic", "scenes", "HeroesReplay.json");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(scene, destination, overwrite: true);
        }

        string ini = Path.Combine(installDirectory, "obs", "Default", "basic.ini");
        if (File.Exists(ini))
        {
            string destination = Path.Combine(
                appData,
                "obs-studio",
                "basic",
                "profiles",
                "HeroesReplay",
                "basic.ini"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(ini, destination, overwrite: true);
        }
    }

    private static string ReleaseSettingsFileName() => "version.txt";

    private static bool IsStreamKey(string file)
    {
        return string.Equals(Path.GetFileName(file), "service.json", StringComparison.OrdinalIgnoreCase);
    }
}
