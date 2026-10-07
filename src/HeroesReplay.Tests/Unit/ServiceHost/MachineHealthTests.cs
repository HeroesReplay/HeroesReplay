using System;
using System.Collections.Generic;
using HeroesReplay.Core.ServiceHost;
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
