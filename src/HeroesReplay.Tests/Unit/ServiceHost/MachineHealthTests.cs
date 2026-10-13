using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MachineHealthTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    [Fact]
    public void Evaluate_IsOkInsideEveryLimit()
    {
        MachineHealthReport report = MachineHealth.Evaluate(
            Snapshot(usedGb: 8, commitGb: 12, agents: 1, conhosts: 12, heroes: 1),
            new MachineHealthSettings()
        );

        Assert.True(report.Ok);
        Assert.Empty(report.Warnings);
        Assert.Equal(50, report.PhysicalUsedPercent);
        Assert.Equal(16 * 1024, report.PhysicalTotalMegabytes);
        Assert.Equal(48, report.CommitPercent);
    }

    [Fact]
    public void Evaluate_WarnsAboveTheCommitAndAgentLimits()
    {
        // Production on 2026-10-07: commit 20.6 of 25.4 GB was 81%, and 83 agents.
        MachineHealthReport report = MachineHealth.Evaluate(
            Snapshot(usedGb: 11.5, commitGb: 22, agents: 83, conhosts: 12, heroes: 1),
            new MachineHealthSettings()
        );

        Assert.False(report.Ok);
        Assert.Equal(2, report.Warnings.Count);
        Assert.Contains("Commit charge is 88%", report.Warnings[0]);
        Assert.Contains("83 Battle.net Agent.exe", report.Warnings[1]);
    }

    [Fact]
    public void Evaluate_ValuesAtTheLimitDoNotWarn()
    {
        MachineHealthReport report = MachineHealth.Evaluate(
            Snapshot(usedGb: 14.4, commitGb: 21.25, agents: 5, conhosts: 40, heroes: 2),
            new MachineHealthSettings()
        );

        Assert.True(report.Ok, string.Join(" ", report.Warnings));
    }

    [Fact]
    public void Evaluate_UsesTheConfiguredLimits()
    {
        var settings = new MachineHealthSettings
        {
            MemoryWarnPercent = 40,
            CommitWarnPercent = 40,
            AgentWarnCount = 0,
            ConhostWarnCount = 5,
            HeroesWarnCount = 0,
        };

        MachineHealthReport report = MachineHealth.Evaluate(
            Snapshot(usedGb: 8, commitGb: 12, agents: 1, conhosts: 12, heroes: 1),
            settings
        );

        Assert.Equal(5, report.Warnings.Count);
        Assert.Contains(report.Warnings, warning => warning.StartsWith("Physical memory"));
        Assert.Contains(report.Warnings, warning => warning.StartsWith("12 conhost.exe"));
        Assert.Contains(
            report.Warnings,
            warning => warning.StartsWith("1 HeroesOfTheStorm_x64.exe")
        );
    }

    [Fact]
    public void Evaluate_DoesNotWarnOnMemoryItCouldNotRead()
    {
        MachineHealthReport report = MachineHealth.Evaluate(
            new MachineHealthSnapshot { AgentProcesses = 1 },
            new MachineHealthSettings()
        );

        Assert.True(report.Ok);
        Assert.Equal(0, report.CommitPercent);
    }

    [Fact]
    public void Settings_FallBackToTheDefaultWatchedProcesses()
    {
        Assert.Equal(
            MachineHealthSettings.DefaultWatchedProcesses,
            new MachineHealthSettings { WatchedProcesses = new List<string> { " " } }.Watched
        );
        Assert.Equal(
            new[] { "obs64" },
            new MachineHealthSettings { WatchedProcesses = new List<string> { "obs64" } }.Watched
        );
    }

    [Fact]
    public void Log_WritesOncePerIntervalWithEachWatchedProcessAndWarning()
    {
        var logger = new ListLogger();
        int reads = 0;
        var log = new MachineHealthLog(
            new MachineHealthSettings { LogInterval = TimeSpan.FromHours(1) },
            () =>
            {
                reads++;
                return Snapshot(usedGb: 8, commitGb: 12, agents: 9, conhosts: 12, heroes: 1) with
                {
                    Processes = new[]
                    {
                        new MachineProcessMemory
                        {
                            Name = "obs64",
                            Pid = 42,
                            PrivateMegabytes = 650,
                        },
                    },
                };
            },
            logger
        );
        var start = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        Assert.True(log.Tick(start));
        Assert.False(log.Tick(start.AddMinutes(59)));
        Assert.True(log.Tick(start.AddHours(1)));

        Assert.Equal(2, reads);
        Assert.Contains(logger.Entries, entry => entry.Message.Contains("obs64 pid 42: 650 MB"));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("9 Battle.net")
        );
    }

    // Production, DESKTOP-8SJEK72, 2026-10-09 04:04: commit 96.8% and one Edge renderer held
    // 13,188 MB private with a 10 MB working set (#399).
    private static readonly MachineProcessMemory EdgeRenderer = new()
    {
        Name = "msedge",
        Pid = 18080,
        PrivateMegabytes = 13188,
        WorkingSetMegabytes = 10,
        StartedAt = new DateTimeOffset(2026, 10, 8, 10, 52, 0, TimeSpan.Zero),
    };

    private static MachineProcessMemory[] ProductionTable() =>
        new[]
        {
            Memory("obs64", 7012, 1450, 900),
            Memory("heroesreplay", 21960, 999, 410),
            Memory("svchost", 1200, 60, 20),
            EdgeRenderer,
            Memory("HeroesOfTheStorm_x64", 30020, 2900, 2600),
            Memory("aspire-managed", 5120, 520, 300),
            Memory("Idle", 0, 0, 0),
            Memory("explorer", 4410, 140, 180),
        };

    [Fact]
    public void TopConsumers_AreTheLargestPrivateBytesFirstWhoeverOwnsThem()
    {
        IReadOnlyList<MachineProcessMemory> top = MachineHealth.TopConsumers(ProductionTable(), 5);

        Assert.Equal(
            new[] { 18080, 30020, 7012, 21960, 5120 },
            top.Select(process => process.Pid).ToArray()
        );
        Assert.Same(EdgeRenderer, top[0]);
    }

    [Fact]
    public void TopConsumers_BreakTiesByWorkingSetThenPidAndSkipTheIdleProcess()
    {
        IReadOnlyList<MachineProcessMemory> top = MachineHealth.TopConsumers(
            new[]
            {
                Memory("b", 30, 500, 10),
                Memory("a", 20, 500, 10),
                Memory("c", 40, 500, 90),
                Memory("Idle", 0, 9000, 9000),
                null,
            },
            5
        );

        Assert.Equal(new[] { 40, 20, 30 }, top.Select(process => process.Pid).ToArray());
        Assert.Empty(MachineHealth.TopConsumers(ProductionTable(), 0));
        Assert.Empty(MachineHealth.TopConsumers(null, 5));
    }

    [Fact]
    public void Evaluate_NamesTheTopConsumersOnlyAboveTheMemoryOrCommitLimit()
    {
        var settings = new MachineHealthSettings();
        MachineHealthSnapshot calm = Snapshot(
            usedGb: 8,
            commitGb: 12,
            agents: 1,
            conhosts: 12,
            heroes: 1
        ) with
        {
            AllProcesses = ProductionTable(),
        };

        Assert.Empty(MachineHealth.Evaluate(calm, settings).TopConsumers);
        // Another warning (agents) is not memory pressure.
        Assert.Empty(
            MachineHealth.Evaluate(calm with { AgentProcesses = 83 }, settings).TopConsumers
        );

        MachineHealthReport commit = MachineHealth.Evaluate(
            calm with
            {
                CommitBytes = 24 * Gigabyte,
            },
            settings
        );
        Assert.Equal(5, commit.TopConsumers.Count);
        Assert.Equal(18080, commit.TopConsumers[0].Pid);

        MachineHealthReport memory = MachineHealth.Evaluate(
            calm with
            {
                PhysicalAvailableBytes = Gigabyte,
            },
            settings
        );
        Assert.Equal(5, memory.TopConsumers.Count);

        MachineHealthReport three = MachineHealth.Evaluate(
            calm with
            {
                CommitBytes = 24 * Gigabyte,
            },
            new MachineHealthSettings { TopConsumerCount = 3 }
        );
        Assert.Equal(3, three.TopConsumers.Count);
        Assert.Empty(
            MachineHealth
                .Evaluate(
                    calm with
                    {
                        CommitBytes = 24 * Gigabyte,
                    },
                    new MachineHealthSettings { TopConsumerCount = 0 }
                )
                .TopConsumers
        );
    }

    [Fact]
    public void DescribeProcess_NamesPidPrivateBytesWorkingSetAndStart()
    {
        string line = MachineHealth.DescribeProcess(EdgeRenderer);

        Assert.StartsWith("msedge pid 18080: 13188 MB private, 10 MB working set, started ", line);
        Assert.Matches(@"started \d{4}-\d{2}-\d{2} \d{2}:\d{2}$", line);
        Assert.Equal(
            "obs64 pid 7: 1 MB private, 2 MB working set",
            MachineHealth.DescribeProcess(Memory("obs64", 7, 1, 2))
        );
    }

    [Fact]
    public void Compose_CountsTheTableAndJoinsStartTimesByPidAndName()
    {
        var started = new DateTimeOffset(2026, 10, 8, 10, 52, 0, TimeSpan.Zero);
        var table = new[]
        {
            new ProcessTableEntry(18080, 1, "msedge.exe", null, started),
            new ProcessTableEntry(7012, 1, "obs64.exe", null, started.AddDays(-1)),
            // The pid changed hands between the two reads: no start time is borrowed.
            new ProcessTableEntry(4410, 1, "notepad.exe", null, started),
            new ProcessTableEntry(200, 1, "Agent.exe", null, null),
            new ProcessTableEntry(201, 1, "Agent.exe", null, null),
            new ProcessTableEntry(300, 1, "conhost.exe", null, null),
            new ProcessTableEntry(30020, 1, "HeroesOfTheStorm_x64.exe", null, null),
        };
        var memory = new[]
        {
            Memory("msedge", 18080, 13188, 10),
            Memory("obs64", 7012, 1450, 900),
            Memory("explorer", 4410, 140, 180),
            Memory("heroesreplay", 21960, 999, 410),
        };

        MachineHealthSnapshot snapshot = MachineHealthProbe.Compose(
            new MachineHealthSnapshot { CommitBytes = 5 },
            table,
            memory,
            new MachineHealthSettings()
        );

        Assert.Equal(5, snapshot.CommitBytes);
        Assert.Equal(2, snapshot.AgentProcesses);
        Assert.Equal(1, snapshot.ConhostProcesses);
        Assert.Equal(1, snapshot.HeroesProcesses);
        Assert.Equal(4, snapshot.AllProcesses.Count);
        Assert.Equal(started, snapshot.AllProcesses.Single(p => p.Pid == 18080).StartedAt);
        Assert.Null(snapshot.AllProcesses.Single(p => p.Pid == 4410).StartedAt);
        Assert.Null(snapshot.AllProcesses.Single(p => p.Pid == 21960).StartedAt);
        // The watched list keeps the settings' order: heroesreplay, obs64, aspire-managed.
        Assert.Equal(
            new[] { "heroesreplay", "obs64" },
            snapshot.Processes.Select(p => p.Name).ToArray()
        );
        Assert.Equal(900, snapshot.Processes[1].WorkingSetMegabytes);
    }

    [Fact]
    public void Compose_ToleratesAnEmptyOrMissingTable()
    {
        MachineHealthSnapshot snapshot = MachineHealthProbe.Compose(null, null, null, null);

        Assert.Empty(snapshot.AllProcesses);
        Assert.Empty(snapshot.Processes);
        Assert.Equal(0, snapshot.AgentProcesses);
    }

    [Fact]
    public void Log_NamesTheTopConsumersAboveTheLimitWithoutTouchingThem()
    {
        var logger = new ListLogger();
        var log = new MachineHealthLog(
            new MachineHealthSettings(),
            () =>
                Snapshot(usedGb: 15, commitGb: 24.2, agents: 1, conhosts: 12, heroes: 1) with
                {
                    AllProcesses = ProductionTable(),
                },
            logger
        );

        Assert.True(log.Tick(new DateTimeOffset(2026, 10, 9, 3, 4, 0, TimeSpan.Zero)));

        LogEntry top = Assert.Single(
            logger.Entries,
            entry => entry.Message.Contains("holding the most commit")
        );
        Assert.Equal(LogLevel.Warning, top.Level);
        Assert.Contains("the 5 processes", top.Message);
        Assert.Contains("msedge pid 18080: 13188 MB private, 10 MB working set", top.Message);
        Assert.Contains("report only", top.Message);
        Assert.True(
            top.Message.IndexOf("msedge", StringComparison.Ordinal)
                < top.Message.IndexOf("obs64", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Log_DoesNotNameConsumersWhileMemoryAndCommitAreInsideTheLimits()
    {
        var logger = new ListLogger();
        var log = new MachineHealthLog(
            new MachineHealthSettings(),
            () =>
                Snapshot(usedGb: 8, commitGb: 12, agents: 1, conhosts: 12, heroes: 1) with
                {
                    AllProcesses = ProductionTable(),
                },
            logger
        );

        Assert.True(log.Tick(new DateTimeOffset(2026, 10, 9, 3, 4, 0, TimeSpan.Zero)));

        Assert.DoesNotContain(
            logger.Entries,
            entry => entry.Message.Contains("holding the most commit")
        );
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    private static MachineProcessMemory Memory(
        string name,
        int pid,
        long privateMegabytes,
        long workingSetMegabytes
    ) =>
        new()
        {
            Name = name,
            Pid = pid,
            PrivateMegabytes = privateMegabytes,
            WorkingSetMegabytes = workingSetMegabytes,
        };

    private static MachineHealthSnapshot Snapshot(
        double usedGb,
        double commitGb,
        int agents,
        int conhosts,
        int heroes
    ) =>
        new()
        {
            PhysicalTotalBytes = 16 * Gigabyte,
            PhysicalAvailableBytes = 16 * Gigabyte - (long)(usedGb * Gigabyte),
            CommitBytes = (long)(commitGb * Gigabyte),
            CommitLimitBytes = 25 * Gigabyte,
            AgentProcesses = agents,
            ConhostProcesses = conhosts,
            HeroesProcesses = heroes,
        };

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class ListLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
    }
}
