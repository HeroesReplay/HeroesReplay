using System;
using System.Collections.Generic;
using System.IO;
using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Services.Observer;

public enum ClientPresetAction
{
    Keep,
    Write,
    LeaveRunning,
}

/// <summary>
/// AhliObs is written before Heroes starts. A client HeroesSwitcher already launched
/// is left running through its data download. Text that names AhliObs is not the HUD.
/// </summary>
public static class ClientInterfacePlan
{
    public const int MaxDataRestarts = 1;
    public static readonly TimeSpan GameDataDownloadExtension = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan GameDataDownloadCap = TimeSpan.FromMinutes(30);

    public static ClientPresetAction Preset(
        bool presetMatches,
        bool heroesRunning,
        bool buildSealed
    )
    {
        bool needsWrite = !presetMatches || !buildSealed;
        if (!needsWrite)
        {
            return ClientPresetAction.Keep;
        }

        if (heroesRunning)
        {
            return ClientPresetAction.LeaveRunning;
        }

        return ClientPresetAction.Write;
    }

    /// <summary>
    /// HeroesSwitcher owns the process it started, including the data download and
    /// "Preparing game data". AhliObs is written before that process starts.
    /// Closing it here leaves no client.
    /// </summary>
    public static bool RestartAfterGameData(
        bool sawDownload,
        bool downloadVisible,
        bool gameDataStartup,
        bool replayVisible,
        int restarts,
        bool clientBuildMatches = true,
        bool sawGameDataStartup = false
    )
    {
        _ = sawDownload;
        _ = downloadVisible;
        _ = gameDataStartup;
        _ = replayVisible;
        _ = restarts;
        _ = clientBuildMatches;
        _ = sawGameDataStartup;
        return false;
    }

    /// <summary>
    /// Remember "Preparing game data" only while the running exe is the replay's build.
    /// An unreadable sample keeps that latch. A different build clears it.
    /// </summary>
    public static bool LatchGameDataStartup(
        bool alreadyLatched,
        bool gameDataStartup,
        bool clientBuildMatches,
        bool differentBuild
    )
    {
        if (gameDataStartup && clientBuildMatches)
        {
            return true;
        }

        if (differentBuild)
        {
            return false;
        }

        return alreadyLatched;
    }

    /// <summary>
    /// A switcher-launched client is not held back for a second start.
    /// </summary>
    public static bool OwesObserverRestart(
        bool clientBuildMatches,
        bool sawGameDataStartup,
        bool gameDataStartup,
        int restarts
    )
    {
        _ = clientBuildMatches;
        _ = sawGameDataStartup;
        _ = gameDataStartup;
        _ = restarts;
        return false;
    }

    /// <summary>
    /// A loading screen on the newest exe is the version handoff, not the replay.
    /// </summary>
    public static bool MayAcceptReplayScreen(bool clientBuildMatches, bool screenVisible)
    {
        return clientBuildMatches && screenVisible;
    }

    /// <summary>
    /// A download dialog on the newest exe is that handoff. It must not arm an AhliObs restart
    /// that then fires when the replay's own exe appears.
    /// </summary>
    public static bool DownloadBelongsToReplayClient(bool sawDownload, bool clientBuildMatches)
    {
        return sawDownload && clientBuildMatches;
    }

    public static DateTimeOffset ExtendForGameDataDownload(
        DateTimeOffset started,
        DateTimeOffset deadline,
        DateTimeOffset now
    )
    {
        DateTimeOffset cap = started.Add(GameDataDownloadCap);
        DateTimeOffset proposed = now.Add(GameDataDownloadExtension);
        if (proposed < deadline)
        {
            proposed = deadline;
        }

        if (proposed > cap)
        {
            proposed = cap;
        }

        return proposed;
    }

    public static string Newest(IEnumerable<string> versions)
    {
        string newest = null;
        if (versions == null)
        {
            return null;
        }

        foreach (string version in versions)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                continue;
            }

            if (newest == null || GameVersionOrder.IsAtLeast(version, newest))
            {
                newest = version.Trim();
            }
        }

        return newest;
    }

    public static bool IsSealed(string recordedBuild, string newestInstalled)
    {
        if (string.IsNullOrWhiteSpace(newestInstalled))
        {
            return true;
        }

        return string.Equals(
            recordedBuild?.Trim(),
            newestInstalled.Trim(),
            StringComparison.OrdinalIgnoreCase
        );
    }
}

public static class ClientInterfaceSeal
{
    public const string FileName = "client-interface-build.txt";

    public static string FilePath(string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return null;
        }

        return Path.Combine(dataDirectory, FileName);
    }

    public static string Read(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            return File.ReadAllText(path).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(string path, string build)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(build))
        {
            return;
        }

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, build.Trim());
    }
}
