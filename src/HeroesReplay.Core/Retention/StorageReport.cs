using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Retention;

/// <summary>One item a pruner's next pass removes.</summary>
public sealed class StoragePlannedItem
{
    public string Path { get; set; }
    public long Bytes { get; set; }
    public DateTimeOffset LastWriteUtc { get; set; }
    public string Reason { get; set; }
    public bool IsDirectory { get; set; }
}

/// <summary>One pruner in <c>storage report</c>: on or off, and what its next pass removes.</summary>
public sealed class StoragePrunerPlan
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Enabled { get; set; }
    public int Count { get; set; }
    public long Bytes { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<StoragePlannedItem> Items { get; set; } = new();
}

/// <summary><c>storage report --output json</c> details.</summary>
public sealed class StorageReportDetails
{
    public string DataDirectory { get; set; }
    public string StateDirectory { get; set; }
    public long DataBytes { get; set; }
    public long StateBytes { get; set; }
    public long? FreeBytes { get; set; }

    /// <summary><c>Housekeeping:DryRun</c>: a pass logs and removes nothing.</summary>
    public bool DryRun { get; set; }

    /// <summary><c>Housekeeping:Enabled</c>: the supervisor runs a pass.</summary>
    public bool Scheduled { get; set; }
    public long IntervalSeconds { get; set; }
    public List<StorageCategoryUsage> Categories { get; set; } = new();
    public List<StoragePrunerPlan> Pruners { get; set; } = new();

    /// <summary>The last recorded pass (<c>housekeeping.json</c>), before this command ran.</summary>
    public HousekeepingRun LastRun { get; set; }

    /// <summary>The pass <c>--apply</c> ran. Null without it.</summary>
    public HousekeepingRun Applied { get; set; }
}

/// <summary>
/// <c>heroesreplay storage report</c> (#487): every category's files, bytes and oldest item, and
/// what each pruner's next pass removes. Read only; <c>--apply</c> runs the pass first.
/// </summary>
public static class StorageReport
{
    public const string ReportCode = "storage.report";
    public const string AppliedCode = "storage.applied";
    public const string DryRunCode = "storage.dry_run";
    public const string ApplyFailedCode = "storage.apply_failed";

    public static StorageReportDetails Build(
        AppSettings settings,
        HousekeepingPass pass,
        DateTimeOffset utcNow,
        HousekeepingRun lastRun = null,
        HousekeepingRun applied = null
    )
    {
        ArgumentNullException.ThrowIfNull(pass);
        HousekeepingSettings housekeeping = settings?.Housekeeping ?? new HousekeepingSettings();
        string state = Housekeeping.StateDirectory(settings);
        IReadOnlyList<StorageCategory> categories = StorageInventory.Categories(settings, state);
        List<StorageCategoryUsage> usage = StorageInventory.Measure(categories);
        IReadOnlyList<PrunePlan> plans = pass.Plan(utcNow);
        StorageInventory.AddPlanned(categories, usage, plans);
        StorageSummary summary = Housekeeping.Summarize(
            settings?.Location?.DataDirectory,
            state,
            categories,
            usage,
            utcNow
        );
        return new StorageReportDetails
        {
            DataDirectory = settings?.Location?.DataDirectory,
            StateDirectory = state,
            DataBytes = summary.DataBytes,
            StateBytes = summary.StateBytes,
            FreeBytes = summary.FreeBytes,
            DryRun = housekeeping.DryRun,
            Scheduled = housekeeping.Enabled,
            IntervalSeconds = (long)housekeeping.EffectiveInterval.TotalSeconds,
            Categories = usage,
            Pruners = pass
                .Pruners.Select(pruner =>
                    Describe(
                        pruner,
                        pass.IsEnabled(pruner),
                        plans.FirstOrDefault(plan =>
                            string.Equals(
                                plan.Pruner,
                                pruner.Name,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                )
                .ToList(),
            LastRun = lastRun,
            Applied = applied,
        };
    }

    public static CliResult<StorageReportDetails> Result(StorageReportDetails details)
    {
        HousekeepingRun applied = details?.Applied;
        bool failed = applied?.Outcome == HousekeepingRun.Failed;
        string code =
            applied == null ? ReportCode
            : failed ? ApplyFailedCode
            : applied.DryRun ? DryRunCode
            : AppliedCode;
        int planned = details?.Pruners.Sum(pruner => pruner.Count) ?? 0;
        long plannedBytes = details?.Pruners.Sum(pruner => pruner.Bytes) ?? 0;
        string message =
            applied != null ? "Housekeeping pass: " + applied.Describe() + "."
            : planned == 0 ? "The next housekeeping pass removes nothing."
            : $"The next housekeeping pass removes {planned} items ({Size.Describe(plannedBytes)}).";
        return new CliResult<StorageReportDetails>
        {
            Ok = !failed,
            Code = code,
            Message = message,
            Environment = CliJson.CurrentEnvironment(),
            Details = details,
        };
    }

    private static StoragePrunerPlan Describe(IPruner pruner, bool enabled, PrunePlan plan)
    {
        var row = new StoragePrunerPlan
        {
            Name = pruner.Name,
            Description = pruner.Description,
            Enabled = enabled,
        };
        if (plan == null)
        {
            return row;
        }

        row.Count = plan.Count;
        row.Bytes = plan.Bytes;
        row.Warnings.AddRange(plan.Warnings);
        row.Items.AddRange(
            plan.TopLevel.Select(item => new StoragePlannedItem
            {
                Path = item.Path,
                Bytes = item.Bytes,
                LastWriteUtc = item.LastWriteUtc,
                Reason = item.Reason,
                IsDirectory = item.IsDirectory,
            })
        );
        return row;
    }
}
