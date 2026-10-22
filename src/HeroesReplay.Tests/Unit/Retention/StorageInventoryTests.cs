using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Retention;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class StorageInventoryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-storage-" + Guid.NewGuid().ToString("N")
    );

    public StorageInventoryTests() => Directory.CreateDirectory(root);

    public void Dispose() => TestTemp.Delete(root);

    private string Data => Path.Combine(root, "Data");
    private string State => Path.Combine(root, "Local");

    [Fact]
    public void Measure_PutsEachFileInItsDeepestCategory_ByExtensionInsideContexts()
    {
        Write(Path.Combine(Data, "Contexts", "10", "match.mp4"), 100, Now.AddDays(-2));
        Write(Path.Combine(Data, "Contexts", "10", "end.png"), 10, Now.AddDays(-5));
        Write(Path.Combine(Data, "Contexts", "10", "youtube-entry.json"), 1, Now);
        Write(Path.Combine(Data, "Standard", "10_x.StormReplay"), 7, Now);
        Write(Path.Combine(Data, "HeroesProfile", "players", "eu-1.json"), 3, Now);
        Write(Path.Combine(Data, "spectated-ids.txt"), 2, Now);
        Write(Path.Combine(State, "updates", "v1", "heroesreplay.exe"), 50, Now);
        Write(Path.Combine(State, "status.json"), 4, Now);
        IReadOnlyList<StorageCategory> categories = StorageInventory.Categories(Settings(), State);

        Dictionary<string, StorageCategoryUsage> usage = StorageInventory
            .Measure(categories)
            .ToDictionary(row => row.Name);

        Assert.Equal(100, usage["recordings"].Bytes);
        Assert.Equal(10, usage["screenshots"].Bytes);
        Assert.Equal(Now.AddDays(-5), usage["screenshots"].Oldest);
        Assert.Equal(1, usage["contexts"].Files);
        Assert.Equal(7, usage["standard"].Bytes);
        Assert.Equal(3, usage["players"].Bytes);
        Assert.Equal(2, usage[StorageInventory.DataOther].Bytes);
        Assert.Equal(50, usage["updates"].Bytes);
        Assert.Equal(4, usage[StorageInventory.LocalOther].Bytes);
        Assert.Equal(0, usage["leases"].Files);
    }

    [Fact]
    public void AddPlanned_CountsEachFileOfARemovedFolder_InItsOwnCategory()
    {
        string context = Path.Combine(Data, "Contexts", "10");
        Write(Path.Combine(context, "match.mp4"), 100, Now);
        Write(Path.Combine(context, "end.png"), 10, Now);
        IReadOnlyList<StorageCategory> categories = StorageInventory.Categories(Settings(), State);
        List<StorageCategoryUsage> usage = StorageInventory.Measure(categories);
        var plan = new PrunePlan("media");
        plan.Add(PruneItem.ForDirectory(new DirectoryInfo(context), "old"));

        StorageInventory.AddPlanned(categories, usage, [plan]);

        Assert.Equal(100, usage.Single(row => row.Name == "recordings").PlannedBytes);
        Assert.Equal(1, usage.Single(row => row.Name == "screenshots").PlannedFiles);
        Assert.Equal(0, usage.Single(row => row.Name == "standard").PlannedFiles);
    }

    [Fact]
    public void Summarize_SplitsDataFromPerUserState_AndNamesTheLargestCategory()
    {
        Write(Path.Combine(Data, "Contexts", "10", "match.mp4"), 100, Now);
        Write(Path.Combine(Data, "spectated-ids.txt"), 2, Now);
        Write(Path.Combine(State, "updates", "v1", "heroesreplay.exe"), 50, Now);
        AppSettings settings = Settings();

        StorageSummary summary = Housekeeping.Summarize(settings, Now);

        Assert.Equal(102, summary.DataBytes);
        Assert.Equal(50, summary.StateBytes);
        Assert.Equal("recordings", summary.LargestCategory);
        Assert.NotNull(summary.FreeBytes);
    }

    [Fact]
    public void Report_ShowsEveryPrunerAndItsItems_AndChangesNothing()
    {
        string replay = Write(
            Path.Combine(Data, "Standard", "10_x.StormReplay"),
            7,
            Now.AddDays(-40)
        );
        File.WriteAllText(Path.Combine(Data, "spectated-ids.txt"), "10" + Environment.NewLine);
        AppSettings settings = Settings();
        settings.Housekeeping.Pruners[DryRunRecordings.PrunerName] = false;

        StorageReportDetails details = StorageReport.Build(
            settings,
            Housekeeping.CreatePass(settings, NullLogger.Instance),
            DateTimeOffset.UtcNow
        );

        Assert.True(File.Exists(replay));
        StoragePrunerPlan media = details.Pruners.Single(pruner =>
            pruner.Name == MediaRetention.PrunerName
        );
        Assert.True(media.Enabled);
        StoragePlannedItem item = Assert.Single(media.Items);
        Assert.Equal(replay, item.Path);
        Assert.Contains("ReplayKeepDays", item.Reason);
        Assert.False(
            details.Pruners.Single(pruner => pruner.Name == DryRunRecordings.PrunerName).Enabled
        );
        Assert.Equal(1, details.Categories.Single(row => row.Name == "standard").PlannedFiles);

        CliResult<StorageReportDetails> result = StorageReport.Result(details);
        Assert.True(result.Ok);
        Assert.Equal(StorageReport.ReportCode, result.Code);
        Assert.Contains("removes 1 items", result.Message);
    }

    [Fact]
    public void Result_NamesTheAppliedPassOutcome()
    {
        CliResult<StorageReportDetails> failed = StorageReport.Result(
            new StorageReportDetails
            {
                Applied = new HousekeepingRun { Outcome = HousekeepingRun.Failed },
            }
        );
        CliResult<StorageReportDetails> dryRun = StorageReport.Result(
            new StorageReportDetails
            {
                Applied = new HousekeepingRun
                {
                    Outcome = HousekeepingRun.DryRunOutcome,
                    DryRun = true,
                },
            }
        );

        Assert.False(failed.Ok);
        Assert.Equal(StorageReport.ApplyFailedCode, failed.Code);
        Assert.True(dryRun.Ok);
        Assert.Equal(StorageReport.DryRunCode, dryRun.Code);
    }

    [Fact]
    public void RunAndRecord_WritesHousekeepingJson_ThatReadsBack()
    {
        string replay = Write(
            Path.Combine(Data, "Standard", "10_x.StormReplay"),
            7,
            Now.AddDays(-40)
        );
        File.WriteAllText(Path.Combine(Data, "spectated-ids.txt"), "10" + Environment.NewLine);
        AppSettings settings = Settings();

        HousekeepingState state = Housekeeping.RunAndRecord(
            settings,
            DateTimeOffset.UtcNow,
            Housekeeping.SupervisorTrigger,
            NullLogger.Instance
        );

        Assert.False(File.Exists(replay));
        HousekeepingState read = HousekeepingFile.TryLoad(HousekeepingFile.PathIn(State));
        Assert.NotNull(read);
        Assert.Equal(HousekeepingRun.Ok, read.LastRun.Outcome);
        Assert.Equal(1, read.LastRun.Deleted);
        Assert.Equal(Housekeeping.SupervisorTrigger, read.LastRun.Trigger);
        Assert.Equal(state.Storage.DataBytes, read.Storage.DataBytes);
        Assert.Equal(Housekeeping.Pruners(settings).Count, read.LastRun.Pruners.Count);
    }

    [Fact]
    public void HousekeepingFile_AnEmptyObject_KeepsItsLists()
    {
        string path = Path.Combine(root, "housekeeping.json");
        File.WriteAllText(path, "{\"lastRun\":{}}");

        HousekeepingState read = HousekeepingFile.TryLoad(path);

        Assert.NotNull(read.LastRun.Pruners);
        Assert.Null(HousekeepingFile.TryLoad(Path.Combine(root, "missing.json")));
    }

    private AppSettings Settings() =>
        new()
        {
            Location = new LocationSettings { DataDirectory = Data },
            HeroesProfileApi = new HeroesProfileApiSettings
            {
                StandardCacheDirectoryName = "Standard",
                RequestsCacheDirectoryName = "Requests",
            },
            Retention = new RetentionSettings(),
            Housekeeping = new HousekeepingSettings { StateDirectory = State },
        };

    private static string Write(string path, int bytes, DateTimeOffset written)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }
}
