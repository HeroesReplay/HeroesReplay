using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Tests.Unit.Obs;
using HeroesReplay.Tests.Unit.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceSupervisionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FailedRole_RestartsAfter10s30s2m5m5m_ThenTheBudgetIsExhausted()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        ServiceSupervision supervision = stack.Supervision();
        Assert.True(supervision.Begin());
        Assert.Equal(new[] { "download", "youtube" }, supervision.Supervised);

        var waits = new List<double>();
        for (int crash = 0; crash < 5; crash++)
        {
            int pid = stack.PidOf("download");
            stack.Crash(pid);
            DateTimeOffset down = stack.Clock.Now;
            stack.RunUntil(supervision, () => stack.Launches.Count == crash + 1);
            waits.Add((stack.Launches[crash].At - down).TotalSeconds);
            Assert.NotEqual(pid, stack.PidOf("download"));
            stack.Ticks(supervision, 5);
        }

        Assert.Equal(new double[] { 10, 30, 120, 300, 300 }, waits);
        Assert.All(stack.Launches, launch => Assert.Equal("download", launch.Role));
        Assert.True(stack.Launches.Last().At - Start < TimeSpan.FromMinutes(30));

        // The sixth crash inside 30 minutes: no restart, one error, and the role stays down.
        stack.Crash(stack.PidOf("download"));
        stack.Ticks(supervision, 3600);
        Assert.Equal(5, stack.Launches.Count);
        ServiceRoleRestarts ledger = supervision.Ledger("download");
        Assert.True(ledger.Exhausted);
        Assert.Equal(5, ledger.Count);
        LogEntry error = Assert.Single(stack.Log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains(ServiceHealthCodes.RestartBudgetExhausted, error.Message);
        Assert.Contains("download", error.Message);

        // The untouched role was never restarted.
        Assert.Equal(0, supervision.Ledger("youtube").Count);

        // services status reports the exhausted budget from supervisor.json.
        ServiceStatusReport report = stack.Status(running: true);
        ServiceRoleHealth download = report.Roles.Single(role => role.Role == "download");
        Assert.Equal(ServiceHealthCodes.RestartBudgetExhausted, report.Code);
        Assert.Equal(ServiceHealthCodes.RestartBudgetExhausted, download.Code);
        Assert.Equal(ServiceRoleState.Failed, download.State);
        Assert.True(download.Restarts.BudgetExhausted);
        Assert.Equal(5, download.Restarts.Count);
        Assert.Equal(5, download.Restarts.BudgetLimit);
        Assert.Equal(1800, download.Restarts.BudgetWindowSeconds);
        Assert.Contains("stays down", download.Cause);
        Assert.Contains("services start --supervise", download.Remediation);
        Assert.False(report.Ok);
        Assert.Equal(1, report.ExitCode);
        Assert.Contains("Restart budget exhausted: download.", report.Message);
    }

    [Fact]
    public void SpectateThatUsesItsBudget_MakesObsSafeOnce_AndOtherRolesDoNot()
    {
        using var stack = new FakeStack(("spectate", 100), ("download", 101));
        int calls = 0;
        ServiceSupervision supervision = stack.Supervision(
            closeGame: () => true,
            spectateDown: () =>
            {
                calls++;
                return "Switched OBS to the waiting scene 'waiting-screen'.";
            }
        );
        Assert.True(supervision.Begin());

        Exhaust(stack, supervision, "download");
        Assert.Equal(0, calls);

        Exhaust(stack, supervision, "spectate");
        stack.Ticks(supervision, 600);

        Assert.Equal(1, calls);
        Assert.True(supervision.Ledger("spectate").Exhausted);
        Assert.Contains(
            stack.Log.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("Spectate is down for good")
                && entry.Message.Contains("waiting scene")
        );
    }

    [Fact]
    public void AFailSafeThatThrows_IsLoggedAndTheSupervisorKeepsRunning()
    {
        using var stack = new FakeStack(("spectate", 100));
        ServiceSupervision supervision = stack.Supervision(
            closeGame: () => true,
            spectateDown: () => throw new InvalidOperationException("OBS is gone.")
        );
        Assert.True(supervision.Begin());

        Exhaust(stack, supervision, "spectate");

        Assert.True(supervision.Tick());
        Assert.Contains(
            stack.Log.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("not made safe")
        );
    }

    [Fact]
    public void ARoleDegradedByItsDependencyProbe_IsNeverRestarted_SoAnOutageIsNoRestartLoop()
    {
        // #305: a rejected key or an outage keeps the role up and degraded for as long as it lasts.
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.Dependency = record =>
            record.Name == "download"
                ? new ServiceRoleDependency
                {
                    Name = "Heroes Profile API",
                    State = ServiceDependencyStates.Unreachable,
                    Code = "download.heroesprofile_unreachable",
                    Cause = "Heroes Profile did not answer within 10s.",
                    Remediation = "Nothing to do for a short outage.",
                    CheckedAt = stack.Clock.Now,
                    Since = Start,
                }
                : null;
        ServiceSupervision supervision = stack.Supervision();
        Assert.True(supervision.Begin());

        stack.Ticks(supervision, 3600);

        Assert.Empty(stack.Launches);
        Assert.Empty(stack.Killed);
        Assert.Equal(0, supervision.Ledger("download").Count);
        Assert.DoesNotContain(stack.Log.Entries, entry => entry.Level >= LogLevel.Warning);
        ServiceRoleHealth download = stack
            .Status(running: true)
            .Roles.Single(role => role.Role == "download");
        Assert.Equal(ServiceRoleState.Degraded, download.State);
        Assert.Equal("download.heroesprofile_unreachable", download.CauseCode);
        Assert.Equal("Nothing to do for a short outage.", download.Remediation);
    }

    /// <summary>Crashes <paramref name="role"/> until its restart budget is used up.</summary>
    private static void Exhaust(FakeStack stack, ServiceSupervision supervision, string role)
    {
        int launches = stack.Launches.Count;
        for (int crash = 0; crash < 5; crash++)
        {
            stack.Crash(stack.PidOf(role));
            stack.RunUntil(supervision, () => stack.Launches.Count == launches + crash + 1);
            stack.Ticks(supervision, 5);
        }

        stack.Crash(stack.PidOf(role));
        stack.Ticks(supervision, 3);
        Assert.True(supervision.Ledger(role).Exhausted);
    }

    [Fact]
    public void StaleRole_IsKilledThenRestartedAfterTheBackoff()
    {
        using var stack = new FakeStack(("download", 100));
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Freeze(100);

        stack.Ticks(supervision, 119);
        Assert.Empty(stack.Killed);

        stack.RunUntil(supervision, () => stack.Killed.Count == 1);
        Assert.Equal(new[] { 100 }, stack.Killed);
        Assert.Equal(Start.AddSeconds(120), stack.KilledAt.Single());

        stack.RunUntil(supervision, () => stack.Launches.Count == 1);
        Assert.Equal(TimeSpan.FromSeconds(10), stack.Launches[0].At - stack.KilledAt[0]);
        ServiceRoleRestarts ledger = supervision.Ledger("download");
        Assert.Equal(1, ledger.Count);
        Assert.Equal(ServiceRestartPolicy.StaleReason, ledger.LastReason);
        Assert.Contains(stack.Log.Entries, entry => entry.Message.Contains("is stale"));
    }

    [Fact]
    public void TheObsWatchdog_StartsAMissingObs_AndWritesItsStateToSupervisorJson()
    {
        // #398: OBS is gone and streaming is desired: the supervisor starts it.
        using var stack = new FakeStack(("download", 100));
        var starts = new List<DateTimeOffset>();
        ObsProcessInfo obs = null;
        ObsWatchdog watchdog = Watchdog(
            stack,
            () => obs,
            at =>
            {
                starts.Add(at);
                obs = new ObsProcessInfo(252, at);
            }
        );
        ServiceSupervision supervision = stack.Supervision(obs: watchdog);
        supervision.Begin();

        Assert.True(supervision.Tick());

        Assert.Equal(new[] { Start }, starts);
        ServiceSupervisorState state = ServiceSupervisorFile.TryLoad(stack.StatePath);
        Assert.Equal(1, state.Obs.Restarts);
        Assert.Equal(ObsWatchdogState.ProcessMissingCode, state.Obs.LastCause);
        // The next pass reads the new OBS's websocket; services status shows the watchdog.
        stack.Ticks(supervision, 1);
        ServiceStatusReport report = stack.Status(running: true);
        Assert.Equal(1, report.Supervisor.Obs.Restarts);
        Assert.Contains("\"obs\":", report.ToJson());
        var text = new StringWriter();
        ServiceSupervisor.WriteStatusText(text, report, null);
        Assert.Contains(
            "  OBS watchdog: running, pid 252, websocket answered 0s ago, stream Inactive; restarts 1 of 4 in 30m, last 1s ago (obs.process_missing).",
            text.ToString()
        );
    }

    [Fact]
    public void AStopRequest_NeverLetsTheObsWatchdogStartObs()
    {
        // #398: services stop must not trigger an OBS start or restart.
        using var stack = new FakeStack(("download", 100));
        var starts = new List<DateTimeOffset>();
        ObsWatchdog watchdog = Watchdog(stack, () => null, starts.Add);
        ServiceSupervision supervision = stack.Supervision(obs: watchdog);
        supervision.Begin();
        stack.StopRequested = true;

        Assert.False(supervision.Tick());
        stack.Clock.Now += TimeSpan.FromMinutes(10);
        Assert.False(supervision.Tick());

        Assert.Empty(starts);
        Assert.Equal(
            ObsWatchdogStates.Stopping,
            ServiceSupervisorFile.TryLoad(stack.StatePath).Obs.State
        );
    }

    /// <summary>
    /// A watchdog whose start is the shared <see cref="ObsLauncher"/> (#409) on a fake table and
    /// a fake <c>cmd</c>: the started OBS is whatever <paramref name="find"/> returns afterwards.
    /// </summary>
    private static ObsWatchdog Watchdog(
        FakeStack stack,
        Func<ObsProcessInfo> find,
        Action<DateTimeOffset> start
    )
    {
        var table = new FakeProcessTable();
        var starter = new FakeProcessStarter
        {
            OnRun = _ =>
            {
                start(stack.Clock.Now);
                if (find() is ObsProcessInfo obs)
                {
                    table.Add(
                        FakeProcessTable.Entry(
                            obs.Pid,
                            FakeProcessStarter.CmdPid,
                            FakeProcessTable.Obs,
                            obs.StartedAt
                        )
                    );
                }

                return new ProcessRun(FakeProcessStarter.CmdPid, true, 0);
            },
        };
        return new ObsWatchdog
        {
            Rules = new ServiceRestartSettings { ObsWatchdog = true }.ObsRules(
                new OBSSettings { ExecutablePath = @"C:\obs\obs64.exe" }
            ),
            WatchingSince = Start,
            Desired = () => ObsWatchdogDesire.Yes,
            FindObs = find,
            Probe = () => ObsWatchdogProbe.Answered(new JObject { ["outputActive"] = false }),
            Launcher = new ObsLauncher(starter, table, null, NullLogger.Instance)
            {
                GateName = LaunchGateProbe.NewName(),
                ExecutableExists = _ => true,
                Now = () => stack.Clock.Now,
                Wait = _ => { },
            },
        };
    }

    [Fact]
    public void StopRequest_EndsTheLoopBeforeAnyRestart()
    {
        using var stack = new FakeStack(("download", 100));
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);
        stack.Ticks(supervision, 3);
        Assert.NotNull(supervision.Ledger("download").NextRestartAt);

        stack.StopRequested = true;
        Assert.False(supervision.Tick());
        stack.Clock.Now += TimeSpan.FromMinutes(10);
        Assert.False(supervision.Tick());
        Assert.Empty(stack.Launches);
        Assert.Contains(stack.Log.Entries, entry => entry.Message.Contains("stop was requested"));
    }

    [Fact]
    public void Run_ExitsZeroOnAStopRequest_AndRemovesItsStateFile()
    {
        using var stack = new FakeStack(("download", 100));
        int passes = 0;
        ServiceSupervision supervision = stack.Supervision(pause =>
        {
            stack.Clock.Now += pause;
            if (++passes == 5)
            {
                Assert.True(File.Exists(stack.StatePath));
                stack.StopRequested = true;
            }
        });

        Assert.Equal(0, supervision.Run(CancellationToken.None));
        Assert.False(File.Exists(stack.StatePath));
        Assert.Empty(stack.Launches);
    }

    [Fact]
    public void Run_RefusesWhenNothingIsRecorded()
    {
        using var stack = new FakeStack();
        Assert.Equal(1, stack.Supervision().Run(CancellationToken.None));
        Assert.Contains(stack.Log.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public void AStopDuringTheReadyWait_LeavesTheNewRoleInTheLockForServicesStop()
    {
        using var stack = new FakeStack(("download", 100));
        stack.CancelLaunches = true;
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);

        bool running = true;
        for (int pass = 0; pass < 30 && running; pass++)
        {
            running = supervision.Tick();
            stack.Clock.Now += TimeSpan.FromSeconds(1);
        }

        Assert.False(running);
        ServiceProcessRecord recorded = Assert.Single(
            ServiceLockStore.TryLoad(stack.LockPath).Processes
        );
        Assert.Equal(stack.Launches.Single().Pid, recorded.Pid);
        Assert.Equal(0, supervision.Ledger("download").Count);
    }

    [Fact]
    public void AFailedRestart_CountsAgainstTheBudgetAndWaitsTheNextBackoff()
    {
        using var stack = new FakeStack(("download", 100));
        stack.FailLaunches = true;
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);

        stack.RunUntil(supervision, () => stack.Launches.Count == 2);
        Assert.Equal(TimeSpan.FromSeconds(10), stack.Launches[0].At - Start);
        // The failed attempt is seen down on the next pass, one second later, then waits 30 s.
        Assert.Equal(TimeSpan.FromSeconds(31), stack.Launches[1].At - stack.Launches[0].At);
        ServiceRoleRestarts ledger = supervision.Ledger("download");
        Assert.Equal(2, ledger.Count);
        Assert.Equal("download failed: ready timed out.", ledger.LastFailure);
    }

    [Fact]
    public void RestartingSpectate_ClosesTheGameItLeftBehindFirst()
    {
        using var stack = new FakeStack(("spectate", 100));
        var order = new List<string>();
        stack.OnLaunch = role => order.Add("launch " + role);
        ServiceSupervision supervision = stack.Supervision(closeGame: () =>
        {
            order.Add("close game");
            return true;
        });
        supervision.Begin();
        stack.Crash(100);

        stack.RunUntil(supervision, () => stack.Launches.Count == 1);
        Assert.Equal(new[] { "close game", "launch spectate" }, order);
    }

    [Fact]
    public void Restart_ReusesTheServicesStartLaunch()
    {
        ServiceStartupHandshake handshake = ServiceStartupHandshake.ReadyNow();
        handshake.Version = "9.9.9";
        var starts = new List<(string Name, string Arguments, string Nonce)>();
        ServiceProcessRecord seen = null;

        ServiceLaunch launch = ServiceSupervisor.Restart(
            "download",
            @"C:\heroesreplay\heroesreplay.exe",
            (name, arguments) =>
            {
                starts.Add((name, arguments, handshake.Pending.Nonce));
                return 4321;
            },
            pid => null,
            handshake,
            record => seen = record
        );

        Assert.True(launch.Ready);
        Assert.Null(launch.Failure);
        var start = Assert.Single(starts);
        Assert.Equal("heroesprofile download", start.Arguments);
        Assert.Equal(4321, launch.Record.Pid);
        Assert.Equal(start.Nonce, launch.Record.Nonce);
        Assert.Equal("9.9.9", launch.Record.Version);
        Assert.NotNull(launch.Record.ReadyAt);
        Assert.Same(launch.Record, seen);
    }

    [Fact]
    public void Restart_StopsARoleThatStartedButNeverGotReady()
    {
        var stopped = new List<int>();
        var alive = new HashSet<int> { 55 };
        var handshake = new ServiceStartupHandshake
        {
            TryReadReady = _ => null,
            ReadyTimeout = TimeSpan.Zero,
            Wait = _ => { },
            StopStarted = pid =>
            {
                stopped.Add(pid);
                alive.Remove(pid);
            },
        };

        ServiceLaunch launch = ServiceSupervisor.Restart(
            "youtube",
            @"C:\heroesreplay\heroesreplay.exe",
            (name, arguments) => 55,
            pid => alive.Contains(pid) ? "heroesreplay" : null,
            handshake
        );

        Assert.False(launch.Ready);
        Assert.False(launch.Cancelled);
        Assert.False(launch.StillRunning);
        Assert.Equal("youtube failed: ready timed out.", launch.Failure);
        Assert.Equal(new[] { 55 }, stopped);
    }

    [Fact]
    public void Restart_FindsTheChildItsLauncherLost_AndTakesItWhenItGetsReady()
    {
        // #397: on the stream PC the PowerShell launcher was cut off after Start-Process, so no
        // pid came back while spectate pid 21960 ran on. The child has this launch's nonce.
        int polls = 0;
        ServiceProcessRecord asked = null;
        ServiceProcessRecord seen = null;
        var stopped = new List<int>();
        var handshake = new ServiceStartupHandshake
        {
            // The ready file appears only after a few polls: a slow start, not a failed one.
            TryReadReady = record =>
                ++polls < 4 || record.Pid != 21960 ? null : ServiceReadyFile.Immediate(record),
            ReadyTimeout = TimeSpan.FromMinutes(3),
            Wait = _ => { },
            StopStarted = stopped.Add,
            FindStarted = (pending, began) =>
            {
                asked = pending;
                return 21960;
            },
        };

        ServiceLaunch launch = ServiceSupervisor.Restart(
            "spectate",
            @"C:\heroesreplay\heroesreplay.exe",
            (name, arguments) => null,
            pid => pid == 21960 ? "heroesreplay" : null,
            handshake,
            record => seen = record
        );

        Assert.True(launch.Ready);
        Assert.Null(launch.Failure);
        Assert.Equal(21960, launch.Record.Pid);
        Assert.Equal("spectate heroesprofile", asked.Arguments);
        Assert.Equal(asked.Nonce, launch.Record.Nonce);
        Assert.Same(launch.Record, seen);
        Assert.Empty(stopped);
    }

    [Fact]
    public void Restart_KillsALostChildThatNeverGetsReady_SoItIsNeverOrphaned()
    {
        var alive = new HashSet<int> { 21960 };
        var stopped = new List<int>();
        var handshake = new ServiceStartupHandshake
        {
            TryReadReady = _ => null,
            ReadyTimeout = TimeSpan.Zero,
            Wait = _ => { },
            StopStarted = pid =>
            {
                stopped.Add(pid);
                alive.Remove(pid);
            },
            FindStarted = (pending, began) => 21960,
        };

        ServiceLaunch launch = ServiceSupervisor.Restart(
            "spectate",
            @"C:\heroesreplay\heroesreplay.exe",
            (name, arguments) => null,
            pid => alive.Contains(pid) ? "heroesreplay" : null,
            handshake
        );

        Assert.False(launch.Ready);
        Assert.False(launch.StillRunning);
        Assert.Equal("spectate failed: ready timed out.", launch.Failure);
        Assert.Equal(new[] { 21960 }, stopped);
        Assert.Empty(alive);
    }

    [Fact]
    public void Restart_SaysWhenAChildThatDidNotGetReadyWouldNotStop()
    {
        var handshake = new ServiceStartupHandshake
        {
            TryReadReady = _ => null,
            ReadyTimeout = TimeSpan.Zero,
            Wait = _ => { },
            StopStarted = _ => throw new InvalidOperationException("Access is denied."),
        };

        ServiceLaunch launch = ServiceSupervisor.Restart(
            "download",
            @"C:\heroesreplay\heroesreplay.exe",
            (name, arguments) => 57,
            pid => "heroesreplay",
            handshake
        );

        Assert.False(launch.Ready);
        Assert.True(launch.StillRunning);
        Assert.Equal(57, launch.Record.Pid);
    }

    [Fact]
    public void Restart_FailsAsBeforeWhenTheLauncherStartedNothing()
    {
        var stopped = new List<int>();
        var handshake = new ServiceStartupHandshake
        {
            TryReadReady = _ => null,
            ReadyTimeout = TimeSpan.FromMinutes(1),
            Wait = _ => { },
            StopStarted = stopped.Add,
            FindStarted = (pending, began) => null,
        };

        ServiceLaunch launch = ServiceSupervisor.Restart(
            "download",
            @"C:\heroesreplay\heroesreplay.exe",
            (name, arguments) => null,
            pid => null,
            handshake
        );

        Assert.False(launch.Ready);
        Assert.Null(launch.Record);
        Assert.False(launch.StillRunning);
        Assert.Equal("Failed to start download (heroesprofile download).", launch.Failure);
        Assert.Empty(stopped);
    }

    [Fact]
    public void AnUntrackedLiveRoleProcess_IsTakenOver_AndNotStartedASecondTime()
    {
        using var stack = new FakeStack(("spectate", 100), ("download", 101));
        int gameClosed = 0;
        ServiceSupervision supervision = stack.Supervision(closeGame: () =>
        {
            gameClosed++;
            return true;
        });
        supervision.Begin();
        stack.Crash(100);
        stack.RunUntracked("spectate", 21960);

        stack.RunUntil(supervision, () => stack.PidOf("spectate") == 21960);

        Assert.Empty(stack.Launches);
        Assert.Empty(stack.Killed);
        // It plays: the game it drives is not closed.
        Assert.Equal(0, gameClosed);
        Assert.Equal(Start.AddSeconds(10), stack.Clock.Now);
        ServiceProcessRecord record = ServiceLockStore
            .TryLoad(stack.LockPath)
            .Processes.Single(item => item.Name == "spectate");
        Assert.Equal("n21960", record.Nonce);
        Assert.Equal("spectate heroesprofile", record.Arguments);
        ServiceRoleRestarts ledger = supervision.Ledger("spectate");
        Assert.Equal(1, ledger.Adopted);
        Assert.Equal(21960, ledger.LastAdoptedPid);
        Assert.Equal(0, ledger.Count);
        Assert.Empty(ledger.Recent);
        Assert.Null(ledger.LastFailure);
        Assert.Contains(
            stack.Log.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains(ServiceSupervision.RoleAdoptedCode)
                && entry.Message.Contains("21960")
        );

        // From then on it is supervised like any role: up, and never started twice.
        stack.Ticks(supervision, 600);
        Assert.Empty(stack.Launches);
        ServiceRoleHealth spectate = stack
            .Status(running: true)
            .Roles.Single(role => role.Role == "spectate");
        Assert.Equal(ServiceRoleState.Ready, spectate.State);
        Assert.Equal(21960, spectate.Pid);
        Assert.Equal(1, spectate.Restarts.Adopted);
        Assert.Equal(Start.AddSeconds(10), spectate.Restarts.LastAdoptedAt);
        Assert.Equal(0, spectate.Restarts.BudgetUsed);
    }

    [Fact]
    public void AnUntrackedRoleProcessWithNoHeartbeat_IsKilledBeforeTheRestart()
    {
        using var stack = new FakeStack(("download", 100));
        var order = new List<string>();
        stack.OnLaunch = role => order.Add("launch " + role);
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);
        stack.RunUntracked("download", 501, heartbeat: false);

        stack.RunUntil(supervision, () => stack.Launches.Count == 1);

        Assert.Equal(new[] { 501 }, stack.Killed);
        Assert.False(stack.IsAlive(501));
        Assert.Equal(Start.AddSeconds(10), stack.KilledAt.Single());
        Assert.Equal(stack.Launches.Single().Pid, stack.PidOf("download"));
        Assert.Equal(0, supervision.Ledger("download").Adopted);
        Assert.Contains(
            stack.Log.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains(ServiceSupervision.UntrackedRoleKilledCode)
        );
    }

    [Fact]
    public void ALaunchThatLostItsChild_IsTakenOverOnTheNextRestart_AsASuccess()
    {
        // The 2026-10-09 sequence: restart 2 "failed" while its spectate played on untracked.
        using var stack = new FakeStack(("spectate", 100));
        stack.LoseLaunchedChildren = true;
        ServiceSupervision supervision = stack.Supervision(closeGame: () => true);
        supervision.Begin();
        stack.Crash(100);

        stack.RunUntil(supervision, () => stack.Launches.Count == 1);
        int child = stack.Launches[0].Pid;
        Assert.True(stack.IsAlive(child));
        // services.json does not name the dead pid 100 any more, nor any dead pid.
        ServiceProcessRecord down = ServiceLockStore
            .TryLoad(stack.LockPath)
            .Processes.Single(item => item.Name == "spectate");
        Assert.Equal(0, down.Pid);
        ServiceRoleHealth failed = stack
            .Status(running: true)
            .Roles.Single(role => role.Role == "spectate");
        Assert.Equal(ServiceRoleState.Failed, failed.State);
        Assert.Contains("did not leave one", failed.Cause);
        stack.Ticks(supervision, 1);
        Assert.Contains(
            "The supervisor restarts it in 30s",
            stack.Status(running: true).Roles.Single(role => role.Role == "spectate").Cause
        );

        stack.LoseLaunchedChildren = false;
        stack.RunUntil(supervision, () => stack.PidOf("spectate") == child);

        // The next due restart took the running child over instead of starting a second one.
        Assert.Single(stack.Launches);
        ServiceRoleRestarts ledger = supervision.Ledger("spectate");
        Assert.Equal(1, ledger.Count);
        Assert.Single(ledger.Recent);
        Assert.Equal(1, ledger.Adopted);
        Assert.Null(ledger.LastFailure);
        Assert.False(ledger.Exhausted);
        stack.Ticks(supervision, 3600);
        Assert.Single(stack.Launches);
        Assert.False(supervision.Ledger("spectate").Exhausted);
        Assert.Equal(
            ServiceRoleState.Ready,
            stack.Status(running: true).Roles.Single(role => role.Role == "spectate").State
        );
    }

    [Fact]
    public void ServicesJson_NeverKeepsADeadPidAfterARestartAttempt()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.FailLaunches = true;
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);

        int attempts = 0;
        for (int pass = 0; pass < 3600; pass++)
        {
            Assert.True(supervision.Tick());
            if (stack.Launches.Count > attempts)
            {
                attempts = stack.Launches.Count;
                ServiceProcessRecord record = ServiceLockStore
                    .TryLoad(stack.LockPath)
                    .Processes.Single(item => item.Name == "download");
                Assert.True(
                    record.Pid == 0 || stack.IsAlive(record.Pid),
                    $"services.json names dead pid {record.Pid} after attempt {attempts}."
                );
            }

            stack.Clock.Now += TimeSpan.FromSeconds(1);
        }

        Assert.Equal(5, attempts);
        Assert.True(supervision.Ledger("download").Exhausted);
        ServiceLock snapshot = ServiceLockStore.TryLoad(stack.LockPath);
        Assert.Equal(0, snapshot.Processes.Single(item => item.Name == "download").Pid);
        Assert.Equal(101, snapshot.Processes.Single(item => item.Name == "youtube").Pid);
        ServiceStatusReport report = stack.Status(running: true);
        ServiceRoleHealth download = report.Roles.Single(role => role.Role == "download");
        Assert.Equal(ServiceHealthCodes.RestartBudgetExhausted, download.Code);
        Assert.Contains("No download process is running", download.Cause);
        Assert.Null(download.Pid);
    }

    [Fact]
    public void ARestartWhoseChildWouldNotStop_KeepsItTracked_AndKillsItOnceStale()
    {
        using var stack = new FakeStack(("download", 100));
        stack.StubbornLaunches = true;
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);

        stack.RunUntil(supervision, () => stack.Launches.Count == 1);
        int child = stack.Launches[0].Pid;
        Assert.Equal(child, stack.PidOf("download"));
        Assert.Contains(
            stack.Log.Entries,
            entry =>
                entry.Message.Contains("could not be stopped") && entry.Message.Contains("1000")
        );

        // It writes no heartbeat, so the supervisor kills it as stale, then restarts the role.
        stack.StubbornLaunches = false;
        stack.RunUntil(supervision, () => stack.Killed.Contains(child));
        stack.RunUntil(supervision, () => stack.Launches.Count == 2);
        Assert.False(stack.IsAlive(child));
    }

    [Fact]
    public void UnderMemoryPressure_ARestartWaitsLongerForReady_AndLogsIt()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.Commit = 96.4;
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);
        stack.RunUntil(supervision, () => stack.Launches.Count == 1);

        Assert.Equal(TimeSpan.FromMinutes(3), stack.Requests[0].ReadyTimeout);
        Assert.Contains(
            stack.Log.Entries,
            entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("96.4%")
                && entry.Message.Contains("3m")
                && entry.Message.Contains("instead of 45s")
        );

        stack.Commit = 62;
        stack.Ticks(supervision, 5);
        stack.Crash(101);
        stack.RunUntil(supervision, () => stack.Launches.Count == 2);
        Assert.Equal(TimeSpan.FromSeconds(45), stack.Requests[1].ReadyTimeout);
        Assert.Single(stack.Log.Entries, entry => entry.Message.Contains("instead of 45s"));
    }

    [Fact]
    public void ALongReadyWait_KeepsSupervisorJsonFresh()
    {
        using var stack = new FakeStack(("download", 100));
        stack.ReadyDelay = TimeSpan.FromMinutes(3);
        var ages = new List<TimeSpan>();
        stack.AfterPause = () =>
            ages.Add(stack.Clock.Now - ServiceSupervisorFile.TryLoad(stack.StatePath).UpdatedAt);
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);

        stack.RunUntil(supervision, () => stack.Launches.Count == 1);

        // Another session reads supervisor.json as fresh for 60 s (ServiceSupervisorFile.FreshFor).
        Assert.Equal(36, ages.Count);
        Assert.All(ages, age => Assert.True(age <= TimeSpan.FromSeconds(15), $"{age} old"));
    }

    [Fact]
    public void Restart_LeavesTheRoleRunningWhenAStopCutsTheReadyWaitShort()
    {
        var stopped = new List<int>();
        var handshake = new ServiceStartupHandshake
        {
            TryReadReady = _ => null,
            ReadyTimeout = TimeSpan.FromMinutes(1),
            Wait = _ => { },
            StopStarted = stopped.Add,
            Cancelled = () => true,
        };

        ServiceLaunch launch = ServiceSupervisor.Restart(
            "youtube",
            @"C:\heroesreplay\heroesreplay.exe",
            (name, arguments) => 56,
            pid => "heroesreplay",
            handshake
        );

        Assert.True(launch.Cancelled);
        Assert.False(launch.Ready);
        Assert.Equal(56, launch.Record.Pid);
        Assert.Empty(stopped);
    }

    [Fact]
    public void Mutex_AllowsOneSupervisorAtATime()
    {
        string name = @"Local\HeroesReplay.ServiceSupervisor.Test." + Guid.NewGuid().ToString("N");
        // No state file: a supervisor running on this machine must not answer for the test mutex.
        string state = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-supervisor-{name[6..]}.json"
        );
        Assert.False(ServiceSupervisorFile.IsRunning(name, state));

        using (ServiceSupervisorMutex first = ServiceSupervisorMutex.TryAcquire(name))
        {
            Assert.NotNull(first);
            Assert.True(ServiceSupervisorFile.IsRunning(name, state));
            Assert.Same(
                ServiceSupervisorLiveness.ByMutex,
                ServiceSupervisorFile.Check(name, state)
            );

            // A second supervisor is another process, so another thread here.
            ServiceSupervisorMutex second = null;
            var other = new Thread(() => second = ServiceSupervisorMutex.TryAcquire(name));
            other.Start();
            other.Join();
            Assert.Null(second);
        }

        Assert.False(ServiceSupervisorFile.IsRunning(name, state));
        ServiceSupervisorMutex after = null;
        var again = new Thread(() =>
        {
            after = ServiceSupervisorMutex.TryAcquire(name);
            after?.Dispose();
        });
        again.Start();
        again.Join();
        Assert.NotNull(after);
    }

    [Fact]
    public void StateFile_CarriesTheRulesAndEachRolesRestarts()
    {
        using var stack = new FakeStack(("download", 100));
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);
        stack.RunUntil(supervision, () => stack.Launches.Count == 1);
        stack.Ticks(supervision, 16);

        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(stack.StatePath));
        JsonElement root = json.RootElement;
        Assert.Equal(9999, root.GetProperty("pid").GetInt32());
        Assert.Equal(
            new long[] { 10, 30, 120, 300 },
            root.GetProperty("backoffSeconds").EnumerateArray().Select(item => item.GetInt64())
        );
        Assert.Equal(5, root.GetProperty("budget").GetInt32());
        Assert.Equal(1800, root.GetProperty("budgetWindowSeconds").GetInt64());
        Assert.Equal(120, root.GetProperty("staleRestartAfterSeconds").GetInt64());
        JsonElement role = root.GetProperty("roles")[0];
        Assert.Equal("download", role.GetProperty("role").GetString());
        Assert.Equal(1, role.GetProperty("count").GetInt32());
        Assert.Equal("failed", role.GetProperty("lastReason").GetString());

        ServiceStatusReport report = stack.Status(running: true);
        ServiceRoleHealth download = report.Roles.Single(item => item.Role == "download");
        Assert.Equal(ServiceRoleState.Ready, download.State);
        Assert.Equal(1, download.Restarts.Count);
        Assert.Equal(1, download.Restarts.BudgetUsed);
        Assert.False(download.Restarts.BudgetExhausted);
        Assert.True(report.Supervisor.Running);
        Assert.Equal(new[] { "download" }, report.Supervisor.Supervised);
        Assert.Equal(ServiceHealthCodes.Ready, report.Code);
    }

    [Fact]
    public void Status_SaysWhenTheSupervisorRestartsAFailedRole()
    {
        using var stack = new FakeStack(("download", 100));
        ServiceSupervision supervision = stack.Supervision();
        supervision.Begin();
        stack.Crash(100);
        Assert.True(supervision.Tick());
        stack.Clock.Now += TimeSpan.FromSeconds(4);

        ServiceRoleHealth download = stack
            .Status(running: true)
            .Roles.Single(role => role.Role == "download");

        Assert.Equal(ServiceHealthCodes.Failed, download.Code);
        Assert.Contains(
            "The supervisor restarts it in 6s (restart 1 of 5 in 30m).",
            download.Cause
        );
        Assert.StartsWith("Nothing yet", download.Remediation);
        Assert.Equal(Start.AddSeconds(10), download.Restarts.NextRestartAt);
    }

    /// <summary>
    /// A fake process table, heartbeat files, and launcher around a temp lock and state file.
    /// A live pid answers heroesreplay and beats on the fake clock unless frozen.
    /// </summary>
    private sealed class FakeStack : IDisposable
    {
        private readonly HashSet<int> alive = new();
        private readonly Dictionary<int, DateTimeOffset> frozen = new();

        /// <summary>Live pids that never wrote a ready file.</summary>
        private readonly HashSet<int> silent = new();
        private int nextPid = 1000;

        public FakeStack(params (string Role, int Pid)[] roles)
        {
            LockPath = Path.Combine(
                Path.GetTempPath(),
                $"heroesreplay-services-{Guid.NewGuid():N}.json"
            );
            StatePath = Path.Combine(
                Path.GetTempPath(),
                $"heroesreplay-supervisor-{Guid.NewGuid():N}.json"
            );
            if (roles.Length > 0)
            {
                ServiceLockStore.Save(
                    LockPath,
                    new ServiceLock
                    {
                        StartedAt = Start,
                        Processes = roles.Select(item => Record(item.Role, item.Pid)).ToList(),
                    }
                );
                foreach ((_, int pid) in roles)
                {
                    alive.Add(pid);
                }
            }
        }

        public FakeClock Clock { get; } = new(Start);
        public string LockPath { get; }
        public string StatePath { get; }
        public bool StopRequested { get; set; }
        public bool CancelLaunches { get; set; }
        public bool FailLaunches { get; set; }

        /// <summary>
        /// The stream PC on 2026-10-09 (#397): the launcher reports no pid, but the child it
        /// started runs on, untracked, and heartbeats.
        /// </summary>
        public bool LoseLaunchedChildren { get; set; }

        /// <summary>The launch did not get ready, and its child would not stop.</summary>
        public bool StubbornLaunches { get; set; }

        /// <summary>A launch takes this long to get ready; the ready wait pauses every 5 s.</summary>
        public TimeSpan ReadyDelay { get; set; }

        /// <summary>The commit charge the supervisor reads before a restart.</summary>
        public double? Commit { get; set; }

        /// <summary>Runs after each pause of a slow launch's ready wait.</summary>
        public Action AfterPause { get; set; }
        public Action<string> OnLaunch { get; set; }
        public List<ServiceLaunchRequest> Requests { get; } = new();

        /// <summary>Live role processes services.json does not track, by pid.</summary>
        public Dictionary<int, UntrackedRoleProcess> Untracked { get; } = new();

        /// <summary>The dependency probe each live role reports in its heartbeat (#305).</summary>
        public Func<ServiceProcessRecord, ServiceRoleDependency> Dependency { get; set; }
        public List<(string Role, int Pid, DateTimeOffset At)> Launches { get; } = new();
        public List<int> Killed { get; } = new();
        public List<DateTimeOffset> KilledAt { get; } = new();
        public ListLogger Log { get; } = new();

        public void Crash(int pid) => alive.Remove(pid);

        public void Freeze(int pid) => frozen[pid] = Clock.Now;

        public bool IsAlive(int pid) => alive.Contains(pid);

        /// <summary>
        /// A live <paramref name="role"/> process of this install that services.json does not
        /// track, with a heartbeat the supervisor can read unless <paramref name="heartbeat"/> is
        /// false.
        /// </summary>
        public void RunUntracked(string role, int pid, bool heartbeat = true)
        {
            alive.Add(pid);
            if (!heartbeat)
            {
                silent.Add(pid);
            }

            ServiceProcessRecord process = Record(role, pid);
            process.Nonce = null;
            process.StartedAt = Clock.Now;
            Untracked[pid] = new UntrackedRoleProcess(
                process,
                heartbeat ? Heartbeat(Record(role, pid)) : null
            );
        }

        public int PidOf(string role) =>
            ServiceLockStore.TryLoad(LockPath).Processes.Single(record => record.Name == role).Pid;

        public ServiceSupervision Supervision(
            Action<TimeSpan> wait = null,
            Func<bool> closeGame = null,
            Func<string> spectateDown = null,
            ObsWatchdog obs = null
        ) =>
            new()
            {
                LockPath = LockPath,
                StatePath = StatePath,
                Obs = obs,
                Time = Clock,
                ProcessNameOrNull = pid => alive.Contains(pid) ? "heroesreplay" : null,
                Probe = _ => null,
                ReadHeartbeat = Heartbeat,
                DeleteHeartbeat = _ => { },
                StopRequested = () => StopRequested,
                Launch = Launch,
                Kill = pid =>
                {
                    Killed.Add(pid);
                    KilledAt.Add(Clock.Now);
                    alive.Remove(pid);
                },
                FindUntracked = (role, tracked) =>
                    Untracked
                        .Values.Where(item =>
                            item.Process.Name == role
                            && alive.Contains(item.Process.Pid)
                            && !tracked.Contains(item.Process.Pid)
                        )
                        .Select(item =>
                            item with
                            {
                                Heartbeat = item.Heartbeat == null ? null : Heartbeat(item.Adopt()),
                            }
                        )
                        .ToList(),
                CommitPercent = () => Commit,
                CloseGame = closeGame,
                SpectateDown = spectateDown,
                Wait = wait ?? (pause => Clock.Now += pause),
                Logger = Log,
                Pid = 9999,
                ExecutablePath = @"C:\heroesreplay\heroesreplay.exe",
                Version = "9.9.9",
                LogPath = () => @"C:\logs\supervisor-2026-10-02.log",
            };

        /// <summary>One pass a second until <paramref name="done"/>, for at most an hour.</summary>
        public void RunUntil(ServiceSupervision supervision, Func<bool> done)
        {
            for (int pass = 0; pass < 3600; pass++)
            {
                Assert.True(supervision.Tick());
                if (done())
                {
                    return;
                }

                Clock.Now += TimeSpan.FromSeconds(1);
            }

            Assert.Fail("The supervisor did not get there within an hour.");
        }

        public void Ticks(ServiceSupervision supervision, int seconds)
        {
            for (int pass = 0; pass < seconds; pass++)
            {
                Clock.Now += TimeSpan.FromSeconds(1);
                Assert.True(supervision.Tick());
            }
        }

        public ServiceStatusReport Status(bool running)
        {
            ServiceStatusReport report = ServiceHealthClassifier.Build(
                ServiceLockStore.TryLoad(LockPath),
                pid => alive.Contains(pid) ? "heroesreplay" : null,
                _ => null,
                Heartbeat,
                stopRequested: false,
                Clock.Now,
                new ServiceHealthSettings()
            );
            return ServiceHealthClassifier.WithSupervisor(
                report,
                ServiceSupervisorFile.TryLoad(StatePath),
                running ? ServiceSupervisorLiveness.ByMutex : ServiceSupervisorLiveness.None,
                Clock.Now
            );
        }

        public void Dispose()
        {
            ServiceLockStore.Delete(LockPath);
            ServiceSupervisorFile.Delete(StatePath);
        }

        private ServiceLaunch Launch(ServiceLaunchRequest request)
        {
            string role = request.Role;
            Requests.Add(request);
            OnLaunch?.Invoke(role);
            ServiceProcessRecord record = Record(role, nextPid++);
            Launches.Add((role, record.Pid, Clock.Now));
            if (LoseLaunchedChildren)
            {
                // ServiceSupervisor.Launch's own search missed it too: nothing is recorded.
                RunUntracked(role, record.Pid);
                return new ServiceLaunch(
                    null,
                    false,
                    false,
                    $"Failed to start {role} ({record.Arguments})."
                );
            }

            request.Started(record);
            for (TimeSpan waited = TimeSpan.Zero; waited < ReadyDelay; waited += Pause)
            {
                Clock.Now += Pause;
                request.Waiting?.Invoke();
                AfterPause?.Invoke();
            }

            if (StubbornLaunches)
            {
                alive.Add(record.Pid);
                silent.Add(record.Pid);
                return new ServiceLaunch(
                    record,
                    false,
                    false,
                    $"{role} failed: ready timed out.",
                    StillRunning: true
                );
            }

            if (CancelLaunches)
            {
                return new ServiceLaunch(
                    record,
                    false,
                    true,
                    $"{role}: a stop was requested before it was ready."
                );
            }

            if (FailLaunches)
            {
                return new ServiceLaunch(record, false, false, $"{role} failed: ready timed out.");
            }

            alive.Add(record.Pid);
            record.ReadyAt = Clock.Now;
            return new ServiceLaunch(record, true, false, null);
        }

        private static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);

        private ServiceReadyReport Heartbeat(ServiceProcessRecord record)
        {
            if (!alive.Contains(record.Pid) || silent.Contains(record.Pid))
            {
                return null;
            }

            DateTimeOffset beat = frozen.TryGetValue(record.Pid, out DateTimeOffset at)
                ? at
                : Clock.Now;
            return new ServiceReadyReport
            {
                Role = record.Name,
                Nonce = record.Nonce,
                Pid = record.Pid,
                Readiness = ServiceReadiness.Ready,
                ReadyAt = beat.AddSeconds(-1),
                HeartbeatAt = beat,
                HeartbeatIntervalSeconds = 15,
                LastSuccessfulWorkAt = beat,
                Dependency = Dependency?.Invoke(record),
            };
        }

        private static ServiceProcessRecord Record(string role, int pid) =>
            new()
            {
                Name = role,
                Pid = pid,
                Nonce = "n" + pid,
                Arguments = ServiceProcessPlan.ArgumentsFor(role),
            };
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

    private sealed class FakeClock : TimeProvider
    {
        public FakeClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
