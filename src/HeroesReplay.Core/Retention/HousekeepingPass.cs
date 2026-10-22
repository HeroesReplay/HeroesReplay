using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Retention;

/// <summary>One pruner in one pass.</summary>
public sealed class HousekeepingPrunerRun
{
    public string Name { get; set; }
    public bool Enabled { get; set; }
    public int Planned { get; set; }
    public long PlannedBytes { get; set; }
    public int Deleted { get; set; }
    public long FreedBytes { get; set; }
    public int InUse { get; set; }
    public List<string> Warnings { get; set; } = new();

    /// <summary>The exception message when the pruner failed. Null otherwise.</summary>
    public string Error { get; set; }
}

/// <summary>What one housekeeping pass did (#487). Saved in <c>housekeeping.json</c>.</summary>
public sealed class HousekeepingRun
{
    public const string Ok = "ok";
    public const string WithWarnings = "warnings";
    public const string Failed = "failed";
    public const string DryRunOutcome = "dry-run";

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }

    /// <summary>Who ran it: <c>supervisor</c> or <c>storage report --apply</c>.</summary>
    public string Trigger { get; set; }
    public bool DryRun { get; set; }

    /// <summary><see cref="Ok"/>, <see cref="WithWarnings"/>, <see cref="Failed"/> or <see cref="DryRunOutcome"/>.</summary>
    public string Outcome { get; set; }
    public int Deleted { get; set; }
    public long FreedBytes { get; set; }
    public List<HousekeepingPrunerRun> Pruners { get; set; } = new();

    public string Describe() =>
        DryRun
            ? $"dry run: {Pruners.Sum(pruner => pruner.Planned)} items ({Size.Describe(Pruners.Sum(pruner => pruner.PlannedBytes))}) would be removed"
            : $"{Outcome}: removed {Deleted} items ({Size.Describe(FreedBytes)})"
                + (
                    Pruners.Any(pruner => pruner.Error != null)
                        ? "; failed: "
                            + string.Join(
                                ", ",
                                Pruners
                                    .Where(pruner => pruner.Error != null)
                                    .Select(pruner => pruner.Name)
                            )
                        : string.Empty
                );
}

/// <summary>
/// The housekeeping pass (#487): plans every registered pruner, and applies each plan unless
/// <c>Housekeeping:DryRun</c> is on, when it only logs what it would remove. A pruner that is
/// switched off is skipped, and one that throws is logged and does not stop the others.
/// </summary>
public sealed class HousekeepingPass
{
    /// <summary>A dry run logs this many items per pruner, then how many more.</summary>
    public const int LoggedItems = 20;

    private readonly HousekeepingSettings settings;
    private readonly IReadOnlyList<IPruner> pruners;
    private readonly ILogger logger;

    public HousekeepingPass(
        HousekeepingSettings settings,
        IEnumerable<IPruner> pruners,
        ILogger logger
    )
    {
        this.settings = settings ?? new HousekeepingSettings();
        this.pruners = (pruners ?? Enumerable.Empty<IPruner>()).ToList();
        this.logger = logger;
    }

    public IReadOnlyList<IPruner> Pruners => pruners;

    public bool IsEnabled(IPruner pruner) => settings.IsEnabled(pruner?.Name);

    /// <summary>Every enabled pruner's plan, for <c>storage report</c>. Nothing is removed.</summary>
    public IReadOnlyList<PrunePlan> Plan(DateTimeOffset utcNow)
    {
        var plans = new List<PrunePlan>();
        foreach (IPruner pruner in pruners.Where(IsEnabled))
        {
            try
            {
                plans.Add(pruner.Plan(utcNow));
            }
            catch (Exception e)
            {
                var failed = new PrunePlan(pruner.Name);
                failed.Warnings.Add($"{pruner.Name} could not plan: {e.Message}");
                plans.Add(failed);
            }
        }

        return plans;
    }

    public HousekeepingRun Run(DateTimeOffset utcNow, string trigger, TimeProvider time = null)
    {
        time ??= TimeProvider.System;
        var run = new HousekeepingRun
        {
            StartedAt = utcNow,
            Trigger = trigger,
            DryRun = settings.DryRun,
        };
        foreach (IPruner pruner in pruners)
        {
            var result = new HousekeepingPrunerRun
            {
                Name = pruner.Name,
                Enabled = IsEnabled(pruner),
            };
            run.Pruners.Add(result);
            if (!result.Enabled)
            {
                continue;
            }

            try
            {
                PrunePlan plan = pruner.Plan(utcNow);
                result.Planned = plan.Count;
                result.PlannedBytes = plan.Bytes;
                if (settings.DryRun)
                {
                    result.Warnings.AddRange(plan.Warnings);
                    LogDryRun(plan, logger);
                    continue;
                }

                RetentionSweep sweep = pruner.Apply(plan);
                result.Deleted = sweep.DeletedFiles;
                result.FreedBytes = sweep.FreedBytes;
                result.InUse = sweep.InUse.Count;
                result.Warnings.AddRange(sweep.Warnings);
                Log(pruner, sweep);
            }
            catch (Exception e)
            {
                result.Error = e.Message;
                logger?.LogWarning(e, "Housekeeping: {Pruner} failed.", pruner.Name);
            }
        }

        run.FinishedAt = time.GetUtcNow();
        run.Deleted = run.Pruners.Sum(pruner => pruner.Deleted);
        run.FreedBytes = run.Pruners.Sum(pruner => pruner.FreedBytes);
        run.Outcome =
            run.Pruners.Any(pruner => pruner.Error != null) ? HousekeepingRun.Failed
            : run.DryRun ? HousekeepingRun.DryRunOutcome
            : run.Pruners.Any(pruner => pruner.Warnings.Count > 0) ? HousekeepingRun.WithWarnings
            : HousekeepingRun.Ok;
        logger?.LogInformation(
            "Housekeeping ({Trigger}) {Summary} in {Seconds:0.#} s.",
            trigger,
            run.Describe(),
            (run.FinishedAt - run.StartedAt).TotalSeconds
        );
        return run;
    }

    private void Log(IPruner pruner, RetentionSweep sweep)
    {
        if (sweep.DeletedFiles > 0)
        {
            logger?.LogInformation(
                "Housekeeping: {Pruner} removed {Files} items ({Size}).",
                pruner.Name,
                sweep.DeletedFiles,
                Size.Describe(sweep.FreedBytes)
            );
        }

        foreach (string warning in sweep.Warnings)
        {
            logger?.LogWarning("{RetentionWarning}", warning);
        }

        foreach (string busy in sweep.InUse)
        {
            logger?.LogInformation(
                "{Path} is still open in another process. The next pass removes it.",
                busy
            );
        }
    }

    /// <summary>
    /// <c>Housekeeping:DryRun</c>: what <paramref name="plan"/> would remove, one line per item up
    /// to <see cref="LoggedItems"/>, and nothing removed.
    /// </summary>
    public static void LogDryRun(PrunePlan plan, ILogger logger)
    {
        if (plan == null || logger == null)
        {
            return;
        }

        foreach (string warning in plan.Warnings)
        {
            logger.LogWarning("{RetentionWarning}", warning);
        }

        IReadOnlyList<PruneItem> items = plan.TopLevel;
        if (items.Count == 0)
        {
            return;
        }

        logger.LogInformation(
            "Housekeeping:DryRun is on, so {Pruner} removed nothing. It would remove {Count} items ({Size}).",
            plan.Pruner,
            items.Count,
            Size.Describe(plan.Bytes)
        );
        foreach (PruneItem item in items.Take(LoggedItems))
        {
            logger.LogInformation(
                "Dry run: {Pruner} would remove {Path} ({Size}, last written {Written}): {Reason}.",
                plan.Pruner,
                item.Path,
                Size.Describe(item.Bytes),
                item.LastWriteUtc.ToString("u", CultureInfo.InvariantCulture),
                item.Reason
            );
        }

        if (items.Count > LoggedItems)
        {
            logger.LogInformation(
                "Dry run: {Pruner} would remove {More} more. `heroesreplay storage report --items` lists them all.",
                plan.Pruner,
                items.Count - LoggedItems
            );
        }
    }
}

/// <summary>Byte counts for people: <c>512 B</c>, <c>3.4 MB</c>, <c>70.1 GB</c>.</summary>
public static class Size
{
    public static string Describe(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} B"
            : value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
