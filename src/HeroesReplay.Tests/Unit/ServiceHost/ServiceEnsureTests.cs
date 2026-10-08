using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// #306: <c>services ensure</c> starts only the requested roles that are down, never stops a
/// running role, never mixes builds, and refuses while a stop is pending, a budget is exhausted,
/// a requested role is stale, or a supervisor owns the restart.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceEnsureTests
{
    private const string Exe = @"C:\heroesreplay\app\heroesreplay.exe";
    private const string Version = "1.0.0-720";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EveryRequestedRoleUp_IsANoop_AndStartsNothing()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" });

        Assert.True(report.Ok);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(ServiceEnsureCodes.Noop, report.Code);
        Assert.Empty(stack.Launches);
        Assert.Empty(stack.Killed);
        Assert.All(report.Roles, role => Assert.Equal(ServiceEnsureActions.Running, role.Action));
    }

    [Fact]
    public void ASecondRun_AfterAStart_IsANoop()
    {
        using var stack = new FakeStack(("download", 100));

        ServiceEnsureReport first = stack.Run(new[] { "download", "youtube" });
        ServiceEnsureReport second = stack.Run(new[] { "download", "youtube" });

        Assert.Equal(ServiceEnsureCodes.Started, first.Code);
        Assert.Equal(new[] { "youtube" }, first.Started);
        Assert.Equal(ServiceEnsureCodes.Noop, second.Code);
        Assert.Single(stack.Launches);
    }

    [Fact]
    public void ADegradedRole_IsUp_AndIsLeftAlone()
    {
        using var stack = new FakeStack(("download", 100));
        stack.LastWork["download"] = Now.AddHours(-2);

        ServiceEnsureReport report = stack.Run(new[] { "download" });

        Assert.Equal(ServiceEnsureCodes.Noop, report.Code);
        Assert.Equal(ServiceRoleState.Degraded, Assert.Single(report.Roles).State);
        Assert.Empty(stack.Launches);
    }

    [Fact]
    public void AKilledRole_IsTheOnlyOneStarted_AndRunningRolesAreUntouched()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.Crash(101);

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" });

        Assert.True(report.Ok);
        Assert.Equal(ServiceEnsureCodes.Started, report.Code);
        Assert.Equal(new[] { "youtube" }, report.Started);
        (string role, int pid) = Assert.Single(stack.Launches);
        Assert.Equal("youtube", role);
        Assert.Empty(stack.Killed);
        Assert.Equal(new[] { "n101" }, stack.DeletedHeartbeats);
        ServiceLock saved = ServiceLockStore.TryLoad(stack.LockPath);
        Assert.Equal(100, saved.Processes.Single(record => record.Name == "download").Pid);
        Assert.Equal(pid, saved.Processes.Single(record => record.Name == "youtube").Pid);
        ServiceEnsureRole youtube = report.Roles.Single(item => item.Role == "youtube");
        Assert.Equal(ServiceEnsureActions.Started, youtube.Action);
        Assert.Equal(ServiceRoleState.Failed, youtube.State);
        Assert.Equal(pid, youtube.Pid);
        Assert.Equal(
            ServiceEnsureActions.Running,
            report.Roles.Single(item => item.Role == "download").Action
        );
    }

    [Fact]
    public void ARoleThatWasNeverStarted_IsStartedAndAddedToServicesJson()
    {
        using var stack = new FakeStack(("download", 100));

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" });

        Assert.Equal(ServiceEnsureCodes.Started, report.Code);
        Assert.Equal(
            new[] { "download", "youtube" },
            ServiceLockStore.TryLoad(stack.LockPath).Processes.Select(record => record.Name)
        );
    }

    [Theory]
    [InlineData(@"C:\heroesreplay\app.previous\heroesreplay.exe", Version)]
    [InlineData(Exe, "1.0.0-708")]
    public void ARoleFromAnotherInstallOrVersion_IsAMismatch_AndNothingStarts(
        string path,
        string version
    )
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.Paths[100] = path;
        stack.Versions[100] = version;
        stack.Crash(101);

        ServiceEnsureReport report = stack.Run(new[] { "youtube" });

        Assert.False(report.Ok);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(ServiceEnsureCodes.Mismatch, report.Code);
        Assert.Contains("download pid 100", report.Message);
        Assert.Contains("services stop", report.Remediation);
        Assert.Contains(
            report.Roles,
            role => role.Role == "download" && role.Action == ServiceEnsureActions.Blocked
        );
        // The down role it would have started is blocked too: a refusal starts nothing.
        Assert.Equal(
            ServiceEnsureActions.Blocked,
            report.Roles.Single(role => role.Role == "youtube").Action
        );
        Assert.Empty(ServiceEnsurePlan.ToStart(report));
        Assert.Empty(stack.Launches);
        Assert.Empty(stack.Killed);
    }

    [Fact]
    public void APendingStop_RefusesAndStartsNothing()
    {
        using var stack = new FakeStack(("download", 100));
        stack.StopRequested = true;

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" });

        Assert.Equal(ServiceEnsureCodes.StopPending, report.Code);
        Assert.False(report.Ok);
        Assert.Empty(stack.Launches);
    }

    [Fact]
    public void AnExhaustedRestartBudget_RefusesAndStartsNothing()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.Crash(101);
        stack.Supervisor = new ServiceSupervisorState
        {
            Pid = 77,
            Budget = 5,
            BudgetWindowSeconds = 1800,
            Supervised = new List<string> { "download", "youtube" },
            Roles = new List<ServiceRoleRestarts>
            {
                new()
                {
                    Role = "youtube",
                    Nonce = "n101",
                    Count = 5,
                    Exhausted = true,
                    ExhaustedAt = Now.AddMinutes(-3),
                },
            },
        };

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" });

        Assert.Equal(ServiceEnsureCodes.BudgetExhausted, report.Code);
        Assert.Contains("youtube", report.Message);
        Assert.Contains("services start --supervise", report.Remediation);
        Assert.Empty(stack.Launches);
    }

    [Fact]
    public void ADownRoleWhileASupervisorRuns_IsHandedToIt_NotStarted()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.Crash(101);
        stack.SupervisorRunning = ServiceSupervisorLiveness.ByStateFile;
        stack.Supervisor = new ServiceSupervisorState
        {
            Pid = 77,
            Budget = 5,
            BudgetWindowSeconds = 1800,
            Supervised = new List<string> { "download", "youtube" },
            Roles = new List<ServiceRoleRestarts>
            {
                new()
                {
                    Role = "youtube",
                    Nonce = "n101",
                    NextRestartAt = Now.AddSeconds(25),
                },
            },
        };

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" }, supervise: true);

        Assert.False(report.Ok);
        Assert.Equal(ServiceEnsureCodes.SupervisorRunning, report.Code);
        Assert.Contains("pid 77, seen via supervisor.json", report.Message);
        Assert.Contains("the supervisor restarts it at", report.Message);
        Assert.False(report.SupervisorAttached);
        Assert.Empty(stack.Launches);
        Assert.Empty(stack.Killed);
    }

    [Fact]
    public void ARoleTheRunningSupervisorDoesNotSupervise_SaysSo()
    {
        using var stack = new FakeStack(("download", 100));
        stack.SupervisorRunning = ServiceSupervisorLiveness.ByMutex;
        stack.Supervisor = new ServiceSupervisorState
        {
            Pid = 77,
            Supervised = new List<string> { "download" },
        };

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" });

        Assert.Equal(ServiceEnsureCodes.SupervisorRunning, report.Code);
        Assert.Contains("does not supervise it", report.Message);
        Assert.Empty(stack.Launches);
    }

    [Fact]
    public void EveryRoleUpAndASupervisorRunning_IsANoop_WithSupervise()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.SupervisorRunning = ServiceSupervisorLiveness.ByMutex;

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" }, supervise: true);

        Assert.Equal(ServiceEnsureCodes.Noop, report.Code);
        Assert.True(report.SupervisorRunning);
        Assert.False(report.SupervisorAttached);
    }

    [Fact]
    public void EveryRoleUpWithoutASupervisor_AttachesOneWithSupervise()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" }, supervise: true);

        Assert.True(report.Ok);
        Assert.Equal(ServiceEnsureCodes.Started, report.Code);
        Assert.True(report.SupervisorAttached);
        Assert.Contains("attaching a supervisor", report.Message);
        Assert.Empty(stack.Launches);
    }

    [Fact]
    public void AStaleRole_IsRefused_AndNeverStopped()
    {
        using var stack = new FakeStack(("download", 100));
        stack.Frozen[100] = Now.AddMinutes(-5);

        ServiceEnsureReport report = stack.Run(new[] { "download" });

        Assert.Equal(ServiceEnsureCodes.Stale, report.Code);
        Assert.Contains("never stops a running role", report.Message);
        Assert.Empty(stack.Killed);
        Assert.Empty(stack.Launches);
    }

    [Fact]
    public void AFailedStart_StopsWhatThisEnsureStarted_AndRestoresServicesJson()
    {
        using var stack = new FakeStack(("spectate", 99), ("download", 100), ("youtube", 101));
        stack.Crash(100);
        stack.Crash(101);
        stack.FailRole = "youtube";

        ServiceEnsureReport report = stack.Run(new[] { "spectate", "download", "youtube" });

        Assert.False(report.Ok);
        Assert.Equal(ServiceEnsureCodes.StartFailed, report.Code);
        Assert.Contains("youtube did not start", report.Message);
        Assert.Empty(report.Started);
        int downloadPid = stack.Launches.Single(launch => launch.Role == "download").Pid;
        Assert.Equal(new[] { downloadPid }, stack.Killed);
        Assert.DoesNotContain(99, stack.Killed);
        ServiceLock saved = ServiceLockStore.TryLoad(stack.LockPath);
        Assert.Equal(
            new[] { ("spectate", 99), ("download", 100), ("youtube", 101) },
            saved.Processes.Select(record => (record.Name, record.Pid))
        );
        Assert.Equal(
            ServiceEnsureActions.StartFailed,
            report.Roles.Single(role => role.Role == "download").Action
        );
        Assert.Equal(
            ServiceEnsureActions.Running,
            report.Roles.Single(role => role.Role == "spectate").Action
        );
    }

    [Fact]
    public void AStopDuringAStart_LeavesTheRecordForServicesStop()
    {
        using var stack = new FakeStack(("download", 100));
        stack.CancelRole = "youtube";

        ServiceEnsureReport report = stack.Run(new[] { "download", "youtube" });

        Assert.Equal(ServiceEnsureCodes.StopPending, report.Code);
        Assert.Empty(stack.Killed);
        Assert.Contains(
            ServiceLockStore.TryLoad(stack.LockPath).Processes,
            record => record.Name == "youtube"
        );
    }

    [Fact]
    public void BeforeStart_RunsOnceWithTheRolesToStart()
    {
        using var stack = new FakeStack(("download", 100));

        stack.Run(new[] { "spectate", "download", "youtube" });

        Assert.Equal(new[] { "spectate", "youtube" }, Assert.Single(stack.BeforeStart));
    }

    [Fact]
    public void Json_UsesTheStatusEnvelope()
    {
        using var stack = new FakeStack(("download", 100), ("youtube", 101));
        stack.Crash(101);

        string json = stack.Run(new[] { "download", "youtube" }).ToJson();

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(ServiceEnsureCodes.Started, root.GetProperty("code").GetString());
        JsonElement youtube = root.GetProperty("roles")
            .EnumerateArray()
            .Single(role => role.GetProperty("role").GetString() == "youtube");
        Assert.Equal("started", youtube.GetProperty("action").GetString());
        Assert.Equal("failed", youtube.GetProperty("state").GetString());
        Assert.Equal("youtube", root.GetProperty("started")[0].GetString());
    }

    [Fact]
    public void Text_NamesTheVerdictEachRoleAndTheFix()
    {
        using var stack = new FakeStack(("download", 100));
        stack.StopRequested = true;
        var output = new StringWriter();

        ServiceEnsure.WriteText(output, stack.Run(new[] { "download" }));

        string text = output.ToString();
        Assert.Contains("[service.ensure_stop_pending]", text);
        Assert.Contains("download", text);
        Assert.Contains("Fix: Wait for `heroesreplay services stop`", text);
    }

    [Fact]
    public void TheEnsureLock_AllowsOneAtATime()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-ensure-{Guid.NewGuid():N}.lock"
        );
        using (ServiceEnsureLock first = ServiceEnsureLock.TryAcquire(path))
        {
            Assert.NotNull(first);
            Assert.Null(ServiceEnsureLock.TryAcquire(path));
        }

        using ServiceEnsureLock again = ServiceEnsureLock.TryAcquire(path);
        Assert.NotNull(again);
    }

    private sealed class FakeStack : IDisposable
    {
        private readonly HashSet<int> alive = new();
        private int nextPid = 1000;

        public FakeStack(params (string Role, int Pid)[] roles)
        {
            LockPath = Path.Combine(
                Path.GetTempPath(),
                $"heroesreplay-services-{Guid.NewGuid():N}.json"
            );
            ServiceLockStore.Save(
                LockPath,
                new ServiceLock
                {
                    StartedAt = Now.AddHours(-1),
                    Processes = roles.Select(item => Record(item.Role, item.Pid)).ToList(),
                }
            );
            foreach ((_, int pid) in roles)
            {
                alive.Add(pid);
            }
        }

        public string LockPath { get; }
        public bool StopRequested { get; set; }
        public string FailRole { get; set; }
        public string CancelRole { get; set; }
        public ServiceSupervisorState Supervisor { get; set; }
        public ServiceSupervisorLiveness SupervisorRunning { get; set; } =
            ServiceSupervisorLiveness.None;
        public Dictionary<int, string> Paths { get; } = new();
        public Dictionary<int, string> Versions { get; } = new();
        public Dictionary<int, DateTimeOffset> Frozen { get; } = new();
        public Dictionary<string, DateTimeOffset> LastWork { get; } = new();
        public List<(string Role, int Pid)> Launches { get; } = new();
        public List<int> Killed { get; } = new();
        public List<string> DeletedHeartbeats { get; } = new();
        public List<IReadOnlyList<string>> BeforeStart { get; } = new();

        public void Crash(int pid) => alive.Remove(pid);

        public ServiceEnsureReport Run(IReadOnlyList<string> roles, bool supervise = false)
        {
            var ensure = new ServiceEnsure
            {
                LockPath = LockPath,
                ExecutablePath = Exe,
                Version = Version,
                Roles = roles,
                Supervise = supervise,
                Time = new FixedClock(Now),
                ProcessNameOrNull = pid => alive.Contains(pid) ? "heroesreplay" : null,
                Probe = _ => null,
                ReadHeartbeat = Heartbeat,
                StopRequested = () => StopRequested,
                ReadSupervisor = () => Supervisor,
                SupervisorLiveness = () => SupervisorRunning,
                Launch = Launch,
                BeforeStart = starting => BeforeStart.Add(starting.ToList()),
                Kill = pid =>
                {
                    Killed.Add(pid);
                    alive.Remove(pid);
                },
                DeleteHeartbeat = record => DeletedHeartbeats.Add(record.Nonce),
            };
            return ensure.Apply(ensure.Plan());
        }

        public void Dispose() => ServiceLockStore.Delete(LockPath);

        private ServiceLaunch Launch(string role, Action<ServiceProcessRecord> started)
        {
            ServiceProcessRecord record = Record(role, nextPid++);
            Launches.Add((role, record.Pid));
            started(record);
            if (role == CancelRole)
            {
                return new ServiceLaunch(record, false, true, $"{role}: a stop was requested.");
            }

            if (role == FailRole)
            {
                // ServiceSupervisor.Restart stops a role that did not get ready.
                return new ServiceLaunch(record, false, false, $"{role} failed: ready timed out.");
            }

            alive.Add(record.Pid);
            return new ServiceLaunch(record, true, false, null);
        }

        private ServiceReadyReport Heartbeat(ServiceProcessRecord record)
        {
            if (!alive.Contains(record.Pid))
            {
                return null;
            }

            DateTimeOffset beat = Frozen.TryGetValue(record.Pid, out DateTimeOffset at)
                ? at
                : Now.AddSeconds(-3);
            return new ServiceReadyReport
            {
                Role = record.Name,
                Nonce = record.Nonce,
                Pid = record.Pid,
                Version = Versions.TryGetValue(record.Pid, out string version) ? version : Version,
                ExecutablePath = Paths.TryGetValue(record.Pid, out string path) ? path : Exe,
                Readiness = ServiceReadiness.Ready,
                ReadyAt = Now.AddMinutes(-30),
                HeartbeatAt = beat,
                HeartbeatIntervalSeconds = 15,
                LastSuccessfulWorkAt = LastWork.TryGetValue(record.Name, out DateTimeOffset work)
                    ? work
                    : Now.AddSeconds(-5),
            };
        }

        private static ServiceProcessRecord Record(string role, int pid) =>
            new()
            {
                Name = role,
                Pid = pid,
                Nonce = "n" + pid,
                Arguments = ServiceProcessPlan.ArgumentsFor(role),
                Version = Version,
            };
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
