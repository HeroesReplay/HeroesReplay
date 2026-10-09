using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Shared;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// #409: spectate's OBS was its child, so a tree kill of a stale spectate took the stream down.
/// Every HeroesReplay OBS start now goes through <see cref="ObsLauncher"/>: the gate, the stale
/// crash sentinel, <c>cmd /c start</c>, and the new obs64 by start time. Spectate owns that
/// obs64 by pid and start time. No real process starts: the starter and the table are fakes.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class ObsLauncherTests : IDisposable
{
    private const string Exe = @"C:\Program Files\obs-studio\bin\64bit\obs64.exe";
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 7, 21, 0, TimeSpan.Zero);
    private static readonly string Arguments = ObsLaunchDecision.ArgumentsFor(
        "HeroesReplay",
        "HeroesReplay"
    );

    private readonly FakeProcessTable table = new();
    private readonly FakeProcessStarter starter = new();
    private readonly string gate = LaunchGateProbe.NewName();
    private readonly string sentinels = Path.Combine(
        Path.GetTempPath(),
        "hr-obs-launch-" + Guid.NewGuid().ToString("N")
    );
    private readonly ListLogger log = new();
    private readonly List<TimeSpan> waits = new();

    public ObsLauncherTests() => Directory.CreateDirectory(sentinels);

    public void Dispose() => TestTemp.Delete(sentinels);

    [Fact]
    public void Launch_StartsObsThroughCmdStart_InsideTheGate_AfterTheStaleSentinel()
    {
        string stale = Stale();
        bool gateHeld = false;
        bool sentinelLeft = true;
        starter.OnRun = start =>
        {
            gateHeld = LaunchGateProbe.HeldElsewhere(gate);
            sentinelLeft = File.Exists(stale);
            table.Add(Obs(7001, T0.AddSeconds(0.4)));
            return new ProcessRun(FakeProcessStarter.CmdPid, true, 0);
        };

        ObsLaunch launch = Launcher().Launch(Exe, Arguments);

        Assert.Equal(ObsLaunchOutcome.Started, launch.Outcome);
        Assert.Equal(7001, launch.Obs.Pid);
        Assert.Equal(T0.AddSeconds(0.4), launch.Obs.StartTime);
        ProcessStartInfo start = Assert.Single(starter.Starts);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "cmd.exe"), start.FileName);
        Assert.Equal(
            "/d /c start \"\" /D \"C:\\Program Files\\obs-studio\\bin\\64bit\" \"C:\\Program Files\\obs-studio\\bin\\64bit\\obs64.exe\" --profile \"HeroesReplay\" --collection \"HeroesReplay\"",
            start.Arguments
        );
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.Equal(@"C:\Program Files\obs-studio\bin\64bit", start.WorkingDirectory);
        Assert.DoesNotContain("--startstreaming", start.Arguments, StringComparison.Ordinal);
        Assert.True(gateHeld);
        Assert.False(sentinelLeft);
        Assert.Contains(
            log.Lines,
            line =>
                line.Level == LogLevel.Information
                && line.Message.Contains("obs64 pid 7001", StringComparison.Ordinal)
                && line.Message.Contains($"parent pid {FakeProcessStarter.CmdPid}")
                && line.Message.Contains("not a child of this process", StringComparison.Ordinal)
        );
        Assert.False(LaunchGateProbe.HeldElsewhere(gate));
    }

    [Fact]
    public void Launch_ObsAlreadyRunning_StartsNothing_AndLeavesItsSentinel()
    {
        string live = Stale();
        table.Add(Obs(9000, T0.AddHours(-3)));

        ObsLaunch launch = Launcher().Launch(Exe, Arguments);

        Assert.Equal(ObsLaunchOutcome.AlreadyRunning, launch.Outcome);
        Assert.Equal(9000, launch.Obs.Pid);
        Assert.Empty(starter.Starts);
        Assert.True(File.Exists(live));
    }

    [Fact]
    public void Launch_TakesTheNewestObsThatStartedAfterTheLaunch()
    {
        starter.OnRun = start =>
        {
            table.Add(Obs(7001, T0.AddSeconds(0.2)));
            table.Add(Obs(7003, T0.AddSeconds(0.9)));
            return new ProcessRun(FakeProcessStarter.CmdPid, true, 0);
        };

        ObsLaunch launch = Launcher().Launch(Exe, Arguments);

        Assert.Equal(ObsLaunchOutcome.Started, launch.Outcome);
        Assert.Equal(7003, launch.Obs.Pid);
    }

    [Fact]
    public void Launch_NoNewObsWithinTheWait_Fails_AndAnOlderObsIsNotTakenForIt()
    {
        // An obs64 that started before the launch (here it shows only after the check) is not ours.
        starter.OnRun = start =>
        {
            table.Add(Obs(7002, T0.AddMinutes(-10)));
            return new ProcessRun(FakeProcessStarter.CmdPid, true, 0);
        };

        ObsLaunch launch = Launcher().Launch(Exe, Arguments);

        Assert.Equal(ObsLaunchOutcome.Failed, launch.Outcome);
        Assert.Null(launch.Obs);
        Assert.Contains("no new obs64 appeared within 10s", launch.Detail);
        Assert.Equal(
            ObsLauncher.DefaultAppearWait,
            waits.Aggregate(TimeSpan.Zero, (a, b) => a + b)
        );
    }

    [Fact]
    public void Launch_AnotherLaunchHoldsTheGate_StartsNothing()
    {
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task holder = Task.Factory.StartNew(
            () =>
            {
                using ObsLaunchGate other = ObsLaunchGate.Enter(TimeSpan.FromSeconds(5), gate);
                held.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            },
            TaskCreationOptions.LongRunning
        );
        try
        {
            Assert.True(held.Wait(TimeSpan.FromSeconds(5)));

            ObsLaunch launch = Launcher(gateWait: TimeSpan.FromMilliseconds(50))
                .Launch(Exe, Arguments);

            Assert.Equal(ObsLaunchOutcome.Failed, launch.Outcome);
            Assert.Equal("another OBS launch held the launch gate.", launch.Detail);
            Assert.Empty(starter.Starts);
        }
        finally
        {
            release.Set();
            holder.Wait(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(null, false, null, "cmd.exe did not start.")]
    [InlineData(FakeProcessStarter.CmdPid, false, null, "cmd.exe did not return within 15s.")]
    [InlineData(FakeProcessStarter.CmdPid, true, 1, "cmd.exe exited 1.")]
    public void Launch_CmdThatFails_IsAFailedLaunch(
        int? pid,
        bool exited,
        int? exitCode,
        string detail
    )
    {
        starter.OnRun = start => new ProcessRun(pid, exited, exitCode);

        ObsLaunch launch = Launcher().Launch(Exe, Arguments);

        Assert.Equal(ObsLaunchOutcome.Failed, launch.Outcome);
        Assert.Equal(detail, launch.Detail);
    }

    [Fact]
    public void Launch_MissingExecutable_StartsNothing()
    {
        ObsLaunch launch = Launcher(exists: false).Launch(Exe, Arguments);

        Assert.Equal(ObsLaunchOutcome.Failed, launch.Outcome);
        Assert.Contains("Set OBS:ExecutablePath", launch.Detail);
        Assert.Empty(starter.Starts);
    }

    [Fact]
    public void SpectatesLaunch_OwnsTheNewObs_AndClosesOnlyThatProcess()
    {
        starter.OnRun = start =>
        {
            table.Add(Obs(7001, T0.AddSeconds(0.4)));
            return new ProcessRun(FakeProcessStarter.CmdPid, true, 0);
        };
        var closed = new List<int>();
        var process = Spectate(closed);

        ObsLaunchDecision started = process.Start(Decision());

        Assert.Equal(ObsLaunchKind.Launch, started.Kind);
        Assert.True(started.Started);
        Assert.Single(starter.Starts);
        Assert.True(process.IsOwned);

        process.CloseOwned();

        Assert.Equal(new[] { 7001 }, closed);
        Assert.False(process.IsOwned);
    }

    [Fact]
    public void SpectatesLaunch_APidReusedWithAnotherStartTime_IsNotClosed()
    {
        starter.OnRun = start =>
        {
            table.Add(Obs(7001, T0.AddSeconds(0.4)));
            return new ProcessRun(FakeProcessStarter.CmdPid, true, 0);
        };
        var closed = new List<int>();
        var process = Spectate(closed);
        Assert.True(process.Start(Decision()).Started);

        // OBS exited, and pid 7001 now belongs to another obs64.
        table.Entries.Clear();
        table.Add(Obs(7001, T0.AddHours(2)));

        Assert.False(process.IsOwned);
        process.CloseOwned();
        Assert.Empty(closed);
    }

    [Fact]
    public void SpectatesLaunch_NoNewObs_IsNotStarted_AndNothingIsOwned()
    {
        var closed = new List<int>();
        var process = Spectate(closed);

        ObsLaunchDecision started = process.Start(Decision());

        Assert.False(started.Started);
        Assert.Contains("no new obs64", started.Detail);
        Assert.False(process.IsOwned);
        process.CloseOwned();
        Assert.Empty(closed);
    }

    [Fact]
    public void SpectatesLaunch_ObsStartedMeanwhile_IsAlreadyRunning_AndNotOwned()
    {
        table.Add(Obs(9000, T0.AddSeconds(-1)));
        var process = Spectate(new List<int>());

        ObsLaunchDecision started = process.Start(Decision());

        Assert.Equal(ObsLaunchKind.AlreadyRunning, started.Kind);
        Assert.False(started.Started);
        Assert.False(process.IsOwned);
        Assert.Empty(starter.Starts);
    }

    private static ProcessTableEntry Obs(int pid, DateTimeOffset started) =>
        FakeProcessTable.Entry(pid, FakeProcessStarter.CmdPid, FakeProcessTable.Obs, started);

    private static ObsLaunchDecision Decision() =>
        ObsLaunchDecision.Decide(true, false, Exe, true, "HeroesReplay", "HeroesReplay");

    private string Stale()
    {
        string path = Path.Combine(sentinels, "run_81b110f2-0000-0000-0000-000000000000");
        File.WriteAllText(path, "");
        return path;
    }

    private ObsLauncher Launcher(bool exists = true, TimeSpan? gateWait = null) =>
        new(starter, table, new ObsCrashSentinel(sentinels, table.HasObs, NullLogger.Instance), log)
        {
            GateName = gate,
            GateWait = gateWait ?? TimeSpan.FromSeconds(5),
            ExecutableExists = _ => exists,
            Now = () => T0,
            Wait = waits.Add,
        };

    private WindowsObsProcess Spectate(List<int> closed) =>
        new(
            Launcher(),
            table,
            obs =>
            {
                closed.Add(obs.Pid);
                return true;
            },
            table.HasObs
        );

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
