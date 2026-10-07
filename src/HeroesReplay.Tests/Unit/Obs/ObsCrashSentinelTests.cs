using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.Obs;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// OBS 32 leaves <c>.sentinel\run_&lt;uuid&gt;</c> behind when it does not exit cleanly, and the
/// next start waits on the Crash Detected dialog. Seen on ASA-SERVER with OBS 32.2.2.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsCrashSentinelTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "hr-obs-sentinel-" + Guid.NewGuid().ToString("N")
    );

    public ObsCrashSentinelTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ObsDown_DeletesEveryStaleRunFileAndLogsEach()
    {
        DateTime written = new(2026, 10, 3, 9, 15, 0, DateTimeKind.Utc);
        string older = Touch("run_81b110f2-1111-2222-3333-444455556666", written);
        string killed = Touch("run_b0c325dc-1111-2222-3333-444455556666", DateTime.UtcNow);
        var logger = new ListLogger();

        IReadOnlyList<ObsSentinelRemoval> removed = new ObsCrashSentinel(
            directory,
            () => false,
            logger
        ).RemoveStale();

        Assert.False(File.Exists(older));
        Assert.False(File.Exists(killed));
        Assert.Equal(
            new[]
            {
                "run_81b110f2-1111-2222-3333-444455556666",
                "run_b0c325dc-1111-2222-3333-444455556666",
            },
            removed.Select(r => r.FileName).Order()
        );
        Assert.Equal(
            written,
            removed
                .Single(r => r.FileName.StartsWith("run_81b110f2", StringComparison.Ordinal))
                .LastWriteUtc
        );
        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Information));
        Assert.Contains(
            logger.Entries,
            e =>
                e.Level == LogLevel.Information
                && e.Message.Contains("run_81b110f2", StringComparison.Ordinal)
                && e.Message.Contains("2026-10-03 09:15:00Z", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ObsRunning_LeavesEverySentinel()
    {
        string live = Touch("run_b0c325dc-1111-2222-3333-444455556666", DateTime.UtcNow);
        var logger = new ListLogger();

        IReadOnlyList<ObsSentinelRemoval> removed = new ObsCrashSentinel(
            directory,
            () => true,
            logger
        ).RemoveStale();

        Assert.Empty(removed);
        Assert.True(File.Exists(live));
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Information);
    }

    [Fact]
    public void MissingFolder_IsFineAndDoesNotAskAboutObs()
    {
        bool asked = false;
        string missing = Path.Combine(directory, "no-such", ".sentinel");

        IReadOnlyList<ObsSentinelRemoval> removed = new ObsCrashSentinel(
            missing,
            () => asked = true,
            new ListLogger()
        ).RemoveStale();

        Assert.Empty(removed);
        Assert.False(asked);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void OnlyRunFilesAreDeleted()
    {
        string run = Touch("run_0f0f0f0f-1111-2222-3333-444455556666", DateTime.UtcNow);
        string other = Touch("crash_0f0f0f0f.txt", DateTime.UtcNow);
        string prefixed = Touch("xrun_0f0f0f0f", DateTime.UtcNow);
        string nested = Path.Combine(directory, "run_folder");
        Directory.CreateDirectory(nested);

        IReadOnlyList<ObsSentinelRemoval> removed = new ObsCrashSentinel(
            directory,
            () => false,
            new ListLogger()
        ).RemoveStale();

        Assert.Equal("run_0f0f0f0f-1111-2222-3333-444455556666", Assert.Single(removed).FileName);
        Assert.False(File.Exists(run));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(prefixed));
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public void DefaultDirectory_IsTheObsConfigSentinelFolder()
    {
        Assert.Equal(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "obs-studio",
                ".sentinel"
            ),
            ObsCrashSentinel.DefaultDirectory()
        );
    }

    private string Touch(string name, DateTime writtenUtc)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, "");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return path;
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
