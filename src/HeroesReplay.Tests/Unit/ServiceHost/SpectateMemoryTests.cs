using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SpectateMemoryTests
{
    private const long Megabyte = 1024L * 1024;

    [Fact]
    public void Trend_FlagsPrivateBytesThatRoseAtEverySessionEndInARow()
    {
        var trend = new SpectateMemoryTrend(new ServiceHealthSettings());

        // Production pid 21960 went 484 -> 999 -> 1304 MB in about 3 h (#399), and kept going.
        SpectateMemoryVerdict[] verdicts = new long[] { 484, 999, 1304, 1350, 1420, 1500 }
            .Select(megabytes => trend.Add(megabytes * Megabyte))
            .ToArray();

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, verdicts.Select(v => v.RisingSessions));
        Assert.All(verdicts.Take(5), verdict => Assert.False(verdict.SustainedGrowth));
        Assert.True(verdicts[5].SustainedGrowth);
        Assert.Equal((1500 - 484) * Megabyte, verdicts[5].GrowthBytes);
        Assert.All(verdicts, verdict => Assert.False(verdict.AboveCeiling));
    }

    [Fact]
    public void Trend_ADropStartsTheRunOver()
    {
        var trend = new SpectateMemoryTrend(
            new ServiceHealthSettings { SpectateMemoryGrowthSessions = 3 }
        );

        // Production pid 16380 swung between 440 and 822 MB over 12 h: GC, not growth.
        foreach (long megabytes in new long[] { 440, 600, 822, 450 })
        {
            trend.Add(megabytes * Megabyte);
        }

        SpectateMemoryVerdict verdict = trend.Add(700 * Megabyte);
        Assert.Equal(1, verdict.RisingSessions);
        Assert.Equal(250 * Megabyte, verdict.GrowthBytes);
        Assert.False(verdict.SustainedGrowth);
        Assert.False(trend.Add(700 * Megabyte).SustainedGrowth);
    }

    [Fact]
    public void Trend_SmallRisesAreNotGrowth()
    {
        var trend = new SpectateMemoryTrend(
            new ServiceHealthSettings { SpectateMemoryGrowthSessions = 3 }
        );
        SpectateMemoryVerdict verdict = null;
        foreach (long megabytes in new long[] { 500, 510, 520, 530, 540 })
        {
            verdict = trend.Add(megabytes * Megabyte);
        }

        Assert.Equal(4, verdict.RisingSessions);
        Assert.Equal(40 * Megabyte, verdict.GrowthBytes);
        Assert.False(verdict.SustainedGrowth);
    }

    [Fact]
    public void Trend_WarnsAboveTheCeilingFromTheFirstSession()
    {
        var trend = new SpectateMemoryTrend(new ServiceHealthSettings());

        Assert.False(trend.Add(2048 * Megabyte).AboveCeiling);
        SpectateMemoryVerdict above = trend.Add(2049 * Megabyte);
        Assert.True(above.AboveCeiling);
        Assert.Equal(2048 * Megabyte, above.CeilingBytes);

        var configured = new SpectateMemoryTrend(
            new ServiceHealthSettings { SpectatePrivateBytesWarn = 600 * Megabyte }
        );
        Assert.True(configured.Add(601 * Megabyte).AboveCeiling);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, -3)]
    public void Settings_ZeroOrLessMeansTheDefaults(long ceiling, int sessions)
    {
        var settings = new ServiceHealthSettings
        {
            SpectatePrivateBytesWarn = ceiling,
            SpectateMemoryGrowthSessions = sessions,
        };

        Assert.Equal(2048 * Megabyte, settings.SpectatePrivateBytesCeiling());
        Assert.Equal(5, settings.SpectateMemoryGrowthRun());
    }

    [Fact]
    public void Log_WritesOneInfoPerSessionEndWithTheManagedSplit()
    {
        var logger = new ListLogger();
        var log = new SpectateMemoryLog(
            new ServiceHealthSettings(),
            () =>
                new SpectateMemorySample
                {
                    PrivateBytes = 999 * Megabyte,
                    WorkingSetBytes = 410 * Megabyte,
                    ManagedHeapBytes = 300 * Megabyte,
                    LargeObjectHeapBytes = 120 * Megabyte,
                    FragmentedBytes = 40 * Megabyte,
                    GcCommittedBytes = 350 * Megabyte,
                    Gen2Collections = 12,
                },
            logger
        );

        SpectateMemoryVerdict verdict = log.SessionEnded(65550003, "Finished");

        Assert.NotNull(verdict);
        LogEntry entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(
            "Spectate memory after session 1 (replay 65550003, Finished): 999 MB private, 410 MB working set, 300 MB managed heap (120 MB large objects, 40 MB fragmented), 350 MB GC committed, 649 MB other private, 12 gen2 collections.",
            entry.Message
        );
    }

    [Fact]
    public void Log_WarnsOnTheCeilingAndOnSustainedGrowthWithoutAnythingElse()
    {
        var logger = new ListLogger();
        var samples = new Queue<long>(new long[] { 900, 1100, 1300, 2100 });
        var log = new SpectateMemoryLog(
            new ServiceHealthSettings { SpectateMemoryGrowthSessions = 3 },
            () => new SpectateMemorySample { PrivateBytes = samples.Dequeue() * Megabyte },
            logger
        );

        for (int session = 0; session < 3; session++)
        {
            log.SessionEnded(session, "Finished");
        }

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);

        SpectateMemoryVerdict last = log.SessionEnded(3, "Finished");

        Assert.True(last.AboveCeiling);
        Assert.True(last.SustainedGrowth);
        LogEntry[] warnings = logger
            .Entries.Where(entry => entry.Level == LogLevel.Warning)
            .ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains(
            "2100 MB, above ServiceHealth:SpectatePrivateBytesWarn (2048 MB)",
            warnings[0].Message
        );
        Assert.Contains("rose at 3 session ends in a row, by 1200 MB", warnings[1].Message);
        Assert.All(warnings, warning => Assert.Contains("not restarted", warning.Message));
    }

    [Fact]
    public void Log_SkipsASessionWhoseMemoryCannotBeRead()
    {
        var logger = new ListLogger();
        var log = new SpectateMemoryLog(
            new ServiceHealthSettings(),
            () => throw new InvalidOperationException("gone"),
            logger
        );

        Assert.Null(log.SessionEnded(1, "Finished"));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Information);
    }

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
