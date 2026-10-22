using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.YouTube.Outbox;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Retention;

/// <summary>
/// The pruners the housekeeping pass runs, and one recorded pass (#487). A new pruner is added to
/// <see cref="Pruners"/>, with its policy in its own section and its switch under
/// <c>Housekeeping:Pruners</c>.
/// </summary>
public static class Housekeeping
{
    public const string SupervisorTrigger = "supervisor";
    public const string ReportTrigger = "storage report --apply";

    public static IReadOnlyList<IPruner> Pruners(AppSettings settings) =>
        [
            MediaRetention.Pruner(settings),
            DryRunRecordings.Pruner(settings),
            ReleaseCache.Pruner(settings, StateDirectory(settings)),
            ReleaseCache.LogsPruner(settings, StateDirectory(settings)),
            ServiceStatePruners.LogsPruner(settings, StateDirectory(settings)),
            ServiceStatePruners.ReadyPruner(settings, StateDirectory(settings)),
            UploadAttemptCleanup.Pruner(settings),
            ReplayLease.Pruner(settings),
            PlayerHeroStatsStore.Pruner(settings),
            CorruptFiles.Pruner(settings),
            ReplayCacheCleanup.Pruner(settings),
            ClientBuildArchive.Pruner(settings),
            HeroesDataCleanup.Pruner(settings),
        ];

    public static HousekeepingPass CreatePass(AppSettings settings, ILogger logger) =>
        new(settings?.Housekeeping, Pruners(settings), logger);

    public static string StateDirectory(AppSettings settings) =>
        (settings?.Housekeeping ?? new HousekeepingSettings()).ResolvedStateDirectory;

    /// <summary>
    /// Runs one pass, measures the disk after it, and writes <c>housekeeping.json</c>. A file
    /// that cannot be written is logged and the pass still counts.
    /// </summary>
    public static HousekeepingState RunAndRecord(
        AppSettings settings,
        DateTimeOffset utcNow,
        string trigger,
        ILogger logger
    )
    {
        HousekeepingRun run = CreatePass(settings, logger).Run(utcNow, trigger);
        var state = new HousekeepingState
        {
            LastRun = run,
            Storage = Summarize(settings, run.FinishedAt),
        };
        string path = HousekeepingFile.PathIn(StateDirectory(settings));
        try
        {
            HousekeepingFile.Save(path, state);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("Could not write {Path}: {Message}", path, e.Message);
        }

        return state;
    }

    /// <summary>The disk by category now: total Data and per-user bytes, free space, the largest category.</summary>
    public static StorageSummary Summarize(AppSettings settings, DateTimeOffset utcNow)
    {
        string state = StateDirectory(settings);
        IReadOnlyList<StorageCategory> categories = StorageInventory.Categories(settings, state);
        List<StorageCategoryUsage> usage = StorageInventory.Measure(categories);
        return Summarize(settings?.Location?.DataDirectory, state, categories, usage, utcNow);
    }

    public static StorageSummary Summarize(
        string dataDirectory,
        string stateDirectory,
        IReadOnlyList<StorageCategory> categories,
        IReadOnlyList<StorageCategoryUsage> usage,
        DateTimeOffset utcNow
    )
    {
        StorageCategoryUsage largest = usage.OrderByDescending(row => row.Bytes).FirstOrDefault();
        return new StorageSummary
        {
            MeasuredAt = utcNow,
            DataDirectory = dataDirectory,
            DataBytes = Sum(categories, usage, dataDirectory),
            StateBytes = Sum(categories, usage, stateDirectory),
            FreeBytes = string.IsNullOrWhiteSpace(dataDirectory)
                ? null
                : StorageInventory.FreeBytes(dataDirectory),
            LargestCategory = largest?.Bytes > 0 ? largest.Name : null,
            LargestBytes = largest?.Bytes ?? 0,
        };
    }

    private static long Sum(
        IReadOnlyList<StorageCategory> categories,
        IReadOnlyList<StorageCategoryUsage> usage,
        string root
    )
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return 0;
        }

        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        HashSet<string> under = categories
            .Where(category =>
            {
                string folder = Path.TrimEndingDirectorySeparator(
                    Path.GetFullPath(category.Directory)
                );
                return string.Equals(folder, full, StringComparison.OrdinalIgnoreCase)
                    || folder.StartsWith(
                        full + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    );
            })
            .Select(category => category.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return usage.Where(row => under.Contains(row.Name)).Sum(row => row.Bytes);
    }
}
