using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;
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
        var handshake = new ServiceStartupHandshake
        {
            TryReadReady = _ => null,
            ReadyTimeout = TimeSpan.Zero,
            Wait = _ => { },
            StopStarted = stopped.Add,
        };

        ServiceLaunch launch = ServiceSupervisor.Restart(
            "youtube",
            @"C:\heroesreplay\heroesreplay.exe",
            (name, arguments) => 55,
            pid => "heroesreplay",
            handshake
        );

        Assert.False(launch.Ready);
        Assert.False(launch.Cancelled);
        Assert.Equal("youtube failed: ready timed out.", launch.Failure);
        Assert.Equal(new[] { 55 }, stopped);
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
        Assert.False(ServiceSupervisorFile.IsRunning(name));

        using (ServiceSupervisorMutex first = ServiceSupervisorMutex.TryAcquire(name))
        {
            Assert.NotNull(first);
            Assert.True(ServiceSupervisorFile.IsRunning(name));

            // A second supervisor is another process, so another thread here.
            ServiceSupervisorMutex second = null;
            var other = new Thread(() => second = ServiceSupervisorMutex.TryAcquire(name));
            other.Start();
            other.Join();
            Assert.Null(second);
        }

        Assert.False(ServiceSupervisorFile.IsRunning(name));
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
        public Action<string> OnLaunch { get; set; }
        public List<(string Role, int Pid, DateTimeOffset At)> Launches { get; } = new();
        public List<int> Killed { get; } = new();
        public List<DateTimeOffset> KilledAt { get; } = new();
        public ListLogger Log { get; } = new();

        public void Crash(int pid) => alive.Remove(pid);

        public void Freeze(int pid) => frozen[pid] = Clock.Now;

        public int PidOf(string role) =>
            ServiceLockStore.TryLoad(LockPath).Processes.Single(record => record.Name == role).Pid;

        public ServiceSupervision Supervision(
            Action<TimeSpan> wait = null,
            Func<bool> closeGame = null
        ) =>
            new()
            {
                LockPath = LockPath,
                StatePath = StatePath,
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
                CloseGame = closeGame,
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
                running,
                Clock.Now
            );
        }

        public void Dispose()
        {
            ServiceLockStore.Delete(LockPath);
            ServiceSupervisorFile.Delete(StatePath);
        }

        private ServiceLaunch Launch(string role, Action<ServiceProcessRecord> started)
        {
            OnLaunch?.Invoke(role);
            ServiceProcessRecord record = Record(role, nextPid++);
            Launches.Add((role, record.Pid, Clock.Now));
            started(record);
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

        private ServiceReadyReport Heartbeat(ServiceProcessRecord record)
        {
            if (!alive.Contains(record.Pid))
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
