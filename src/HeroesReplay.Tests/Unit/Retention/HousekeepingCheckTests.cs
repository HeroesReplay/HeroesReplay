using System;
using HeroesReplay.CLI.Commands.Check;
using HeroesReplay.Core.Retention;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HousekeepingCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static readonly StorageSummary Disk = new()
    {
        DataBytes = 74L * 1024 * 1024 * 1024,
        StateBytes = 2L * 1024 * 1024 * 1024,
        FreeBytes = 269L * 1024 * 1024 * 1024,
        LargestCategory = "recordings",
        LargestBytes = 70L * 1024 * 1024 * 1024,
    };

    [Fact]
    public void NoPassRecorded_IsAWarning()
    {
        HousekeepingCheckReport report = HousekeepingCheck.Evaluate(
            Disk,
            null,
            new HousekeepingSettings(),
            Now
        );

        Assert.True(report.Warning);
        Assert.Equal(HousekeepingCheck.NeverRan, report.Reason);
        Assert.Contains("largest recordings 70.0 GB", report.Detail);
        Assert.Contains("269.0 GB free", report.Detail);
        Assert.Contains("storage report --apply", report.Detail);
    }

    [Fact]
    public void ARecentPass_IsOk()
    {
        HousekeepingCheckReport report = HousekeepingCheck.Evaluate(
            Disk,
            State(Now.AddMinutes(-30), HousekeepingRun.Ok),
            new HousekeepingSettings(),
            Now
        );

        Assert.False(report.Warning);
        Assert.Null(report.Reason);
        Assert.Contains("(supervisor): ok: removed 3 items", report.Detail);
    }

    [Fact]
    public void APassOlderThanThreeIntervals_IsStale()
    {
        HousekeepingCheckReport report = HousekeepingCheck.Evaluate(
            Disk,
            State(Now.AddHours(-4), HousekeepingRun.Ok),
            new HousekeepingSettings { Interval = TimeSpan.FromHours(1) },
            Now
        );

        Assert.True(report.Warning);
        Assert.Equal(HousekeepingCheck.Stale, report.Reason);
        Assert.Contains("services status", report.Detail);
    }

    [Fact]
    public void AFailedPass_IsAWarning()
    {
        HousekeepingCheckReport report = HousekeepingCheck.Evaluate(
            Disk,
            State(Now.AddMinutes(-5), HousekeepingRun.Failed),
            new HousekeepingSettings(),
            Now
        );

        Assert.True(report.Warning);
        Assert.Equal(HousekeepingCheck.LastFailed, report.Reason);
    }

    [Fact]
    public void CheckResult_AWarningPasses_WithItsStableCode()
    {
        CheckCommand.CheckResult warning = CheckCommand.ToCheckResult(
            HousekeepingCheck.Evaluate(Disk, null, new HousekeepingSettings(), Now)
        );
        CheckCommand.CheckResult ok = CheckCommand.ToCheckResult(
            HousekeepingCheck.Evaluate(
                Disk,
                State(Now, HousekeepingRun.Ok),
                new HousekeepingSettings(),
                Now
            )
        );

        Assert.True(warning.Ok);
        Assert.Equal(CheckCodes.StorageNeverRan, warning.Code);
        Assert.StartsWith(CheckCommand.WarningPrefix, warning.Detail);
        Assert.Equal(CheckCodes.StorageOk, ok.Code);
    }

    private static HousekeepingState State(DateTimeOffset startedAt, string outcome) =>
        new()
        {
            LastRun = new HousekeepingRun
            {
                StartedAt = startedAt,
                FinishedAt = startedAt.AddSeconds(2),
                Trigger = Housekeeping.SupervisorTrigger,
                Outcome = outcome,
                Deleted = 3,
                FreedBytes = 1024,
            },
        };
}
