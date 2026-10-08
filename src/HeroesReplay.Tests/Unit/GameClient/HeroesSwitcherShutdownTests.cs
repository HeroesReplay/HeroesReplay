using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.GameClient;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroesSwitcherShutdownTests
{
    private const string Switcher =
        @"C:\Program Files (x86)\Heroes of the Storm\Support64\HeroesSwitcher_x64.exe";
    private const string Heroes =
        @"C:\Program Files (x86)\Heroes of the Storm\Versions\Base98348\HeroesOfTheStorm_x64.exe";
    private const string BattleNet = @"C:\heroesreplay\Battle.net\Battle.net.exe";
    private static readonly DateTimeOffset Launch = new(2026, 10, 8, 14, 58, 16, TimeSpan.Zero);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    [Fact]
    public void CloseIdle_SwitcherWithoutHeroes_ClosesItsWindowAndDoesNotKill()
    {
        // #359: services stop closed Heroes 40020 and left HeroesSwitcher_x64 40108 running.
        var machine = new FakeMachine(
            new ProcessTableEntry(
                40108,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddSeconds(1)
            )
        );
        machine.OnClose = pid => machine.Exit(pid);

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Equal(new[] { 40108 }, machine.Closed);
        Assert.Empty(machine.Killed);
        SwitcherStop stop = Assert.Single(result.Stopped);
        Assert.Equal(SwitcherStopOutcome.Closed, stop.Outcome);
        Assert.Empty(result.LeftAlone);
        Assert.True(result.AllStopped);
        Assert.Equal("HeroesSwitcher_x64 pid 40108: closed.", result.Describe());
        Assert.True(machine.Waited <= Grace);
    }

    [Fact]
    public void CloseIdle_SwitcherThatIgnoresTheClose_IsKilledAfterTheGrace()
    {
        var machine = new FakeMachine(
            new ProcessTableEntry(
                40108,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddSeconds(1)
            )
        );

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Equal(new[] { 40108 }, machine.Closed);
        Assert.Equal(new[] { 40108 }, machine.Killed);
        Assert.Equal(Grace, machine.Waited);
        Assert.Equal(SwitcherStopOutcome.Killed, Assert.Single(result.Stopped).Outcome);
        Assert.Equal("HeroesSwitcher_x64 pid 40108: killed.", result.Describe());
    }

    [Fact]
    public void CloseIdle_SwitcherWithoutAWindow_IsKilledWithoutTheWait()
    {
        var machine = new FakeMachine(
            new ProcessTableEntry(
                40108,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddSeconds(1)
            )
        )
        {
            HasWindow = false,
        };

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Equal(TimeSpan.Zero, machine.Waited);
        Assert.Equal(new[] { 40108 }, machine.Killed);
        Assert.Equal(SwitcherStopOutcome.Killed, Assert.Single(result.Stopped).Outcome);
    }

    [Fact]
    public void CloseIdle_SwitcherWithALiveHeroesChild_IsLeftAlone()
    {
        var machine = new FakeMachine(
            new ProcessTableEntry(40108, 9000, "HeroesSwitcher_x64.exe", Switcher, Launch),
            new ProcessTableEntry(
                40020,
                40108,
                "HeroesOfTheStorm_x64.exe",
                Heroes,
                Launch.AddSeconds(2)
            )
        );

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Empty(machine.Closed);
        Assert.Empty(machine.Killed);
        Assert.Empty(result.Stopped);
        SwitcherHandoff handoff = Assert.Single(result.LeftAlone);
        Assert.Equal(40108, handoff.Pid);
        Assert.Equal(40020, handoff.HeroesPid);
        Assert.True(result.AllStopped);
        Assert.Equal(
            "HeroesSwitcher_x64 pid 40108: left running, its Heroes pid 40020 is still up.",
            result.Describe()
        );
    }

    [Fact]
    public void CloseIdle_OnlyTheSwitcherWithoutAHeroesChildIsClosed()
    {
        // A Heroes that Battle.net started is not the idle switcher's child, so it does not keep
        // that switcher. The switcher handing off to its own Heroes child is left.
        var machine = new FakeMachine(
            new ProcessTableEntry(9000, 1, "Battle.net.exe", BattleNet, Launch.AddHours(-3)),
            new ProcessTableEntry(
                40108,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddSeconds(1)
            ),
            new ProcessTableEntry(
                41000,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddSeconds(30)
            ),
            new ProcessTableEntry(
                41010,
                41000,
                "HeroesOfTheStorm_x64.exe",
                Heroes,
                Launch.AddSeconds(31)
            ),
            new ProcessTableEntry(
                42000,
                9000,
                "HeroesOfTheStorm_x64.exe",
                Heroes,
                Launch.AddSeconds(40)
            )
        );
        machine.OnClose = pid => machine.Exit(pid);

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Equal(new[] { 40108 }, machine.Closed);
        Assert.Equal(40108, Assert.Single(result.Stopped).Pid);
        Assert.Equal(41000, Assert.Single(result.LeftAlone).Pid);
    }

    [Fact]
    public void CloseIdle_AHeroesOlderThanTheSwitcherOnItsPid_IsNotItsChild()
    {
        // The switcher reused the pid of the process that started this Heroes.
        var machine = new FakeMachine(
            new ProcessTableEntry(
                40108,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddMinutes(10)
            ),
            new ProcessTableEntry(40020, 40108, "HeroesOfTheStorm_x64.exe", Heroes, Launch)
        );
        machine.OnClose = pid => machine.Exit(pid);

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Equal(new[] { 40108 }, machine.Closed);
        Assert.Equal(SwitcherStopOutcome.Closed, Assert.Single(result.Stopped).Outcome);
        Assert.Empty(result.LeftAlone);
    }

    [Fact]
    public void CloseIdle_SwitcherThatStartsHeroesDuringTheWait_IsNotKilled()
    {
        var machine = new FakeMachine(
            new ProcessTableEntry(
                40108,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddSeconds(1)
            )
        );
        machine.OnWait = () =>
            machine.Start(
                new ProcessTableEntry(
                    40500,
                    40108,
                    "HeroesOfTheStorm_x64.exe",
                    Heroes,
                    Launch.AddSeconds(3)
                )
            );

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Empty(machine.Killed);
        Assert.Empty(result.Stopped);
        SwitcherHandoff handoff = Assert.Single(result.LeftAlone);
        Assert.Equal(40108, handoff.Pid);
        Assert.Equal(40500, handoff.HeroesPid);
    }

    [Fact]
    public void CloseIdle_SwitcherThatCannotBeKilled_IsStillRunning()
    {
        var machine = new FakeMachine(
            new ProcessTableEntry(
                40108,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddSeconds(1)
            )
        )
        {
            KillResult = ProcessKillResult.AccessDenied,
        };

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        SwitcherStop stop = Assert.Single(result.Stopped);
        Assert.Equal(SwitcherStopOutcome.StillRunning, stop.Outcome);
        Assert.False(result.AllStopped);
        Assert.Equal(
            "HeroesSwitcher_x64 pid 40108: still running (kill: AccessDenied).",
            result.Describe()
        );
    }

    [Fact]
    public void CloseIdle_APidReusedDuringTheWait_IsNotKilled()
    {
        var machine = new FakeMachine(
            new ProcessTableEntry(
                40108,
                9000,
                "HeroesSwitcher_x64.exe",
                Switcher,
                Launch.AddSeconds(1)
            )
        );
        // The switcher exits and a new switcher gets its pid before the next read.
        machine.OnWait = () =>
        {
            machine.Exit(40108);
            machine.Start(
                new ProcessTableEntry(
                    40108,
                    9000,
                    "HeroesSwitcher_x64.exe",
                    Switcher,
                    Launch.AddSeconds(4)
                )
            );
        };

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Empty(machine.Killed);
        Assert.Equal(SwitcherStopOutcome.Closed, Assert.Single(result.Stopped).Outcome);
    }

    [Fact]
    public void CloseIdle_NoSwitcher_DoesNothing()
    {
        var machine = new FakeMachine(
            new ProcessTableEntry(9000, 1, "Battle.net.exe", BattleNet, Launch.AddHours(-3))
        );

        SwitcherStopResult result = machine.Shutdown().CloseIdle();

        Assert.Empty(machine.Closed);
        Assert.Empty(machine.Killed);
        Assert.Empty(result.Stopped);
        Assert.Empty(result.LeftAlone);
        Assert.Equal(string.Empty, result.Describe());
    }

    /// <summary>
    /// A process table. CloseMainWindow and a kill act on it only through the hooks, and every
    /// wait runs <see cref="OnWait"/> and adds to <see cref="Waited"/>; nothing sleeps.
    /// </summary>
    private sealed class FakeMachine
    {
        private readonly List<ProcessTableEntry> table;

        public FakeMachine(params ProcessTableEntry[] processes)
        {
            table = processes.ToList();
        }

        public bool HasWindow { get; init; } = true;
        public ProcessKillResult KillResult { get; init; } = ProcessKillResult.Killed;
        public Action<int> OnClose { get; set; }
        public Action OnWait { get; set; }
        public List<int> Closed { get; } = new();
        public List<int> Killed { get; } = new();
        public TimeSpan Waited { get; private set; }

        public void Exit(int pid) => table.RemoveAll(process => process.Pid == pid);

        public void Start(ProcessTableEntry process) => table.Add(process);

        public HeroesSwitcherShutdown Shutdown() =>
            new(
                () => table.ToList(),
                process =>
                {
                    Closed.Add(process.Pid);
                    if (!HasWindow)
                    {
                        return false;
                    }

                    OnClose?.Invoke(process.Pid);
                    return true;
                },
                process =>
                {
                    Killed.Add(process.Pid);
                    if (KillResult == ProcessKillResult.Killed)
                    {
                        Exit(process.Pid);
                    }

                    return KillResult;
                },
                pause =>
                {
                    Waited += pause;
                    Action once = OnWait;
                    OnWait = null;
                    once?.Invoke();
                },
                Grace
            );
    }
}
