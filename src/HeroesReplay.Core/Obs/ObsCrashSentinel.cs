using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Obs;

/// <summary>One stale OBS run sentinel that was deleted.</summary>
public sealed record ObsSentinelRemoval(string FileName, DateTime LastWriteUtc);

/// <summary>
/// OBS 32 writes <c>%APPDATA%\obs-studio\.sentinel\run_&lt;uuid&gt;</c> when it starts and deletes
/// it on a clean exit. A <c>run_*</c> file left by a crash, a power loss, or a kill makes the next
/// start stop on the "OBS Studio Crash Detected" dialog until a person answers, and the websocket
/// does not start meanwhile. Before HeroesReplay launches OBS, and only while no OBS runs, those
/// files are stale and are deleted. Portable OBS installs are not handled.
/// </summary>
public sealed class ObsCrashSentinel
{
    public const string FilePattern = "run_*";

    private readonly string directory;
    private readonly Func<bool> obsRunning;
    private readonly ILogger logger;

    public ObsCrashSentinel(string directory, Func<bool> obsRunning, ILogger logger)
    {
        this.directory = directory;
        this.obsRunning = obsRunning ?? throw new ArgumentNullException(nameof(obsRunning));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary><c>%APPDATA%\obs-studio\.sentinel</c>.</summary>
    public static string DefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obs-studio",
            ".sentinel"
        );

    /// <summary>
    /// Deletes every <c>run_*</c> file in the sentinel folder when no OBS process runs. A running
    /// OBS owns its sentinel, so nothing is touched then. A missing folder is not an error.
    /// </summary>
    public IReadOnlyList<ObsSentinelRemoval> RemoveStale()
    {
        var removed = new List<ObsSentinelRemoval>();
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return removed;
        }

        if (obsRunning())
        {
            logger.LogDebug("OBS is running. Its crash sentinels in {Directory} stay.", directory);
            return removed;
        }

        foreach (string path in Directory.EnumerateFiles(directory, FilePattern))
        {
            var file = new FileInfo(path);
            if (!file.Name.StartsWith("run_", StringComparison.Ordinal))
            {
                continue;
            }

            DateTime written = file.LastWriteTimeUtc;
            try
            {
                file.Delete();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(e, "Could not delete the stale OBS sentinel {File}.", file.Name);
                continue;
            }

            removed.Add(new ObsSentinelRemoval(file.Name, written));
            logger.LogInformation(
                "Deleted stale OBS crash sentinel {File} (written {Written:u}) so OBS starts without the Crash Detected dialog.",
                file.Name,
                written
            );
        }

        return removed;
    }
}
