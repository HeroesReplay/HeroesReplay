using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Retention;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Retention;

[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class HousekeepingPassTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-housekeeping-" + Guid.NewGuid().ToString("N")
    );

    public HousekeepingPassTests() => Directory.CreateDirectory(root);

    public void Dispose() => TestTemp.Delete(root);

    [Fact]
    public void Run_AppliesEachEnabledPruner_AndSkipsOneSwitchedOff()
    {
        string old = File("old.bin", 10);
        string other = File("other.bin", 20);
        var settings = new HousekeepingSettings();
        settings.Pruners["second"] = false;
        var pass = new HousekeepingPass(
            settings,
            [Planning("first", old), Planning("second", other)],
            NullLogger.Instance
        );

        HousekeepingRun run = pass.Run(Now, Housekeeping.SupervisorTrigger);

        Assert.False(System.IO.File.Exists(old));
        Assert.True(System.IO.File.Exists(other));
        Assert.Equal(HousekeepingRun.Ok, run.Outcome);
        Assert.Equal(1, run.Deleted);
        Assert.Equal(10, run.FreedBytes);
        HousekeepingPrunerRun second = run.Pruners.Single(pruner => pruner.Name == "second");
        Assert.False(second.Enabled);
        Assert.Equal(0, second.Planned);
    }

    [Fact]
    public void Run_InDryRun_RemovesNothingAndLogsEachItem()
    {
        string old = File("old.bin", 10);
        var log = new ListLogger();
        var pass = new HousekeepingPass(
            new HousekeepingSettings { DryRun = true },
            [Planning("first", old)],
            log
        );

        HousekeepingRun run = pass.Run(Now, Housekeeping.SupervisorTrigger);

        Assert.True(System.IO.File.Exists(old));
        Assert.Equal(HousekeepingRun.DryRunOutcome, run.Outcome);
        Assert.Equal(0, run.Deleted);
        Assert.Equal(1, run.Pruners[0].Planned);
        Assert.Equal(10, run.Pruners[0].PlannedBytes);
        Assert.Contains(log.Entries, entry => entry.Contains("would remove " + old));
    }

    [Fact]
    public void Run_APrunerThatThrows_FailsThePass_AndTheOthersStillRun()
    {
        string old = File("old.bin", 10);
        var pass = new HousekeepingPass(
            new HousekeepingSettings(),
            [
                new Pruner("broken", "throws", _ => throw new IOException("disk gone")),
                Planning("first", old),
            ],
            NullLogger.Instance
        );

        HousekeepingRun run = pass.Run(Now, Housekeeping.SupervisorTrigger);

        Assert.Equal(HousekeepingRun.Failed, run.Outcome);
        Assert.Equal("disk gone", run.Pruners[0].Error);
        Assert.False(System.IO.File.Exists(old));
        Assert.Contains("failed: broken", run.Describe());
    }

    [Fact]
    public void Plan_CountsAFileInsideARemovedFolderOnce_AndAppliesFilesBeforeTheFolder()
    {
        string folder = Path.Combine(root, "context");
        Directory.CreateDirectory(folder);
        string video = Path.Combine(folder, "match.mp4");
        System.IO.File.WriteAllBytes(video, new byte[64]);
        System.IO.File.WriteAllBytes(Path.Combine(folder, "end.png"), new byte[16]);
        var plan = new PrunePlan("media");
        plan.Add(PruneItem.ForFile(new FileInfo(video), "uploaded"));
        plan.Add(PruneItem.ForDirectory(new DirectoryInfo(folder), "old folder"));

        Assert.Equal(1, plan.Count);
        Assert.Equal(80, plan.Bytes);
        Assert.True(plan.TopLevel.Single().IsDirectory);

        RetentionSweep sweep = plan.DeleteAll();

        Assert.False(Directory.Exists(folder));
        Assert.Equal(2, sweep.DeletedFiles);
        Assert.Equal(80, sweep.FreedBytes);
    }

    [Fact]
    public void Schedule_RunsOnTheFirstTick_ThenOncePerInterval()
    {
        var passes = new List<DateTimeOffset>();
        var schedule = new HousekeepingSchedule(
            new HousekeepingSettings { Interval = TimeSpan.FromHours(1) },
            passes.Add,
            NullLogger.Instance,
            start: work => work()
        );

        Assert.True(schedule.Tick(Now));
        Assert.False(schedule.Tick(Now.AddMinutes(59)));
        Assert.True(schedule.Tick(Now.AddHours(1)));
        Assert.Equal(new[] { Now, Now.AddHours(1) }, passes);
    }

    [Fact]
    public void Schedule_DoesNotStartASecondPass_WhileOneRuns()
    {
        Action pending = null;
        int runs = 0;
        var schedule = new HousekeepingSchedule(
            new HousekeepingSettings { Interval = TimeSpan.FromMinutes(1) },
            _ => runs++,
            NullLogger.Instance,
            start: work => pending = work
        );

        Assert.True(schedule.Tick(Now));
        Assert.True(schedule.Running);
        Assert.False(schedule.Tick(Now.AddHours(1)));

        pending();
        Assert.False(schedule.Running);
        Assert.True(schedule.Tick(Now.AddHours(1)));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Schedule_Disabled_NeverRuns()
    {
        int runs = 0;
        var schedule = new HousekeepingSchedule(
            new HousekeepingSettings { Enabled = false },
            _ => runs++,
            NullLogger.Instance,
            start: work => work()
        );

        Assert.False(schedule.Tick(Now));
        Assert.Equal(0, runs);
    }

    [Fact]
    public void Schedule_APassThatThrows_IsLogged_AndTheNextOneStillRuns()
    {
        var log = new ListLogger();
        int runs = 0;
        var schedule = new HousekeepingSchedule(
            new HousekeepingSettings { Interval = TimeSpan.FromMinutes(1) },
            _ =>
            {
                runs++;
                throw new InvalidOperationException("boom");
            },
            log,
            start: work => work()
        );

        Assert.True(schedule.Tick(Now));
        Assert.True(schedule.Tick(Now.AddMinutes(1)));
        Assert.Equal(2, runs);
        Assert.Contains(log.Entries, entry => entry.Contains("housekeeping pass failed"));
    }

    [Fact]
    public void Settings_APrunerNotListed_Runs()
    {
        var settings = new HousekeepingSettings();
        settings.Pruners["media"] = false;

        Assert.False(settings.IsEnabled("MEDIA"));
        Assert.True(settings.IsEnabled("dry-run-recordings"));
        Assert.Equal(
            HousekeepingSettings.DefaultInterval,
            new HousekeepingSettings { Interval = TimeSpan.Zero }.EffectiveInterval
        );
    }

    private string File(string name, int bytes)
    {
        string path = Path.Combine(root, name);
        System.IO.File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static IPruner Planning(string name, string path) =>
        new Pruner(
            name,
            name,
            _ =>
            {
                var plan = new PrunePlan(name);
                plan.Add(PruneItem.ForFile(new FileInfo(path), "test"));
                return plan;
            }
        );

    private sealed class ListLogger : ILogger
    {
        public List<string> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add(formatter(state, exception));
    }
}
