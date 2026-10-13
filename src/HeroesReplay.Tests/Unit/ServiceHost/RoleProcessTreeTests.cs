using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// #409: <c>services stop</c> forcing a role and the supervisor's stale, stalled, and untracked
/// kills take the role's process tree, but never an obs64 in it, nor what that OBS started. The
/// table is a fake: nothing real is killed.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class RoleProcessTreeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 7, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Kill_LeavesAnObs64DescendantAndWhatItStarted_AndKillsTheRest()
    {
        var table = new FakeProcessTable();
        // Spectate, started by a PowerShell launcher that has exited.
        table.Add(FakeProcessTable.Entry(100, 50, "heroesreplay.exe", T0));
        table.Add(FakeProcessTable.Entry(101, 100, "conhost.exe", T0.AddSeconds(1)));
        // An OBS an older build started as spectate's child, with its browser page and muxer.
        table.Add(FakeProcessTable.Entry(200, 100, "obs64.exe", T0.AddSeconds(60)));
        table.Add(FakeProcessTable.Entry(201, 200, "obs-browser-page.exe", T0.AddSeconds(62)));
        table.Add(FakeProcessTable.Entry(202, 200, "obs-ffmpeg-mux.exe", T0.AddSeconds(70)));
        table.Add(FakeProcessTable.Entry(102, 100, "HeroesSwitcher_x64.exe", T0.AddSeconds(90)));
        table.Add(FakeProcessTable.Entry(103, 102, "HeroesOfTheStorm_x64.exe", T0.AddSeconds(91)));
        table.Add(FakeProcessTable.Entry(300, 4, "explorer.exe", T0.AddHours(-5)));
        var log = new ListLogger();

        RoleTreeKill kill = new RoleProcessTree(table, log).Kill(100);

        Assert.Equal(new[] { 100, 101, 102, 103 }, table.Killed);
        Assert.Equal(new[] { 100, 101, 102, 103 }, kill.Killed.Select(entry => entry.Pid));
        Assert.Equal(200, Assert.Single(kill.Spared).Pid);
        Assert.Empty(kill.Failed);
        Assert.Equal(
            new[] { 200, 201, 202, 300 },
            table.Entries.Select(entry => entry.Pid).OrderBy(pid => pid)
        );
        (LogLevel level, string message) = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("heroesreplay.exe pid 100", message);
        Assert.Contains("obs64.exe pid 200", message);
        Assert.Contains(RoleProcessTree.ObsSparedCode, message);
        Assert.Contains("obs64.exe pid 200", RoleProcessTree.Describe(100, kill));
    }

    [Fact]
    public void Kill_AProcessWhoseDeadParentsPidWasReused_IsNotTakenForAChild()
    {
        // OBS started detached: its parent was a cmd (pid 100) that exited. Spectate later got
        // pid 100. OBS started before spectate, so it is not spectate's child at all.
        var table = new FakeProcessTable();
        table.Add(FakeProcessTable.Entry(9000, 100, "obs64.exe", T0.AddHours(-2)));
        table.Add(FakeProcessTable.Entry(400, 100, "notepad.exe", T0.AddHours(-1)));
        table.Add(FakeProcessTable.Entry(100, 50, "heroesreplay.exe", T0));
        var log = new ListLogger();

        RoleTreeKill kill = new RoleProcessTree(table, log).Kill(100);

        Assert.Equal(new[] { 100 }, table.Killed);
        Assert.Empty(kill.Spared);
        Assert.Empty(log.Lines);
        Assert.Null(RoleProcessTree.Describe(100, kill));
    }

    [Fact]
    public void Kill_APidThatIsNotRunning_Throws()
    {
        var table = new FakeProcessTable();

        Assert.Throws<ArgumentException>(() => new RoleProcessTree(table).Kill(100));
        Assert.Empty(table.Killed);
    }

    [Fact]
    public void Kill_ARoleThatCannotBeKilled_Throws_AfterTryingItsTree()
    {
        var table = new FakeProcessTable();
        table.Add(FakeProcessTable.Entry(100, 50, "heroesreplay.exe", T0));
        table.Add(FakeProcessTable.Entry(101, 100, "conhost.exe", T0.AddSeconds(1)));
        table.Add(FakeProcessTable.Entry(200, 100, "obs64.exe", T0.AddSeconds(60)));
        table.Unkillable.Add(100);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new RoleProcessTree(table).Kill(100)
        );

        Assert.Contains("AccessDenied", error.Message);
        Assert.Equal(new[] { 101 }, table.Killed);
        Assert.Contains(table.Entries, entry => entry.Pid == 200);
    }

    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Lines.Add((logLevel, formatter(state, exception)));
    }
}
