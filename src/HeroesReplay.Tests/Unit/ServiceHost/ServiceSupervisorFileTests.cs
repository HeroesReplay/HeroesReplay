using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// A <c>Local\</c> mutex is per logon session, so `services status` over SSH never saw the
/// desktop's supervisor and always said it was not running (#283). Without the mutex,
/// supervisor.json decides.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceSupervisorFileTests
{
    private const int SupervisorPid = 14420;
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 11, 26, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ProcessStart = Now - TimeSpan.FromHours(3);
    private static readonly TimeSpan FreshFor = ServiceSupervisorFile.FreshFor(null);

    [Fact]
    public void FreshFor_IsFourHeartbeatIntervals()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), FreshFor);
        Assert.Equal(
            TimeSpan.FromSeconds(120),
            ServiceSupervisorFile.FreshFor(
                new ServiceHealthSettings { HeartbeatInterval = TimeSpan.FromSeconds(30) }
            )
        );
    }

    [Fact]
    public void FreshFileWithTheLivePidAndItsStartTime_IsRunning()
    {
        ServiceSupervisorLiveness liveness = ServiceSupervisorFile.FromStateFile(
            State(updatedAgo: TimeSpan.FromSeconds(12)),
            Live(SupervisorPid, ProcessStart),
            Now,
            FreshFor
        );

        Assert.True(liveness.Running);
        Assert.Equal(ServiceSupervisorLiveness.ViaStateFile, liveness.SeenVia);
        Assert.Equal("mutex not visible from this session", liveness.Detail);
    }

    [Fact]
    public void StaleFile_IsNotRunning()
    {
        // The pid and its start time still match: a hung supervisor is not a running one.
        ServiceSupervisorLiveness liveness = ServiceSupervisorFile.FromStateFile(
            State(updatedAgo: TimeSpan.FromMinutes(5)),
            Live(SupervisorPid, ProcessStart),
            Now,
            FreshFor
        );

        Assert.False(liveness.Running);
        Assert.Equal(ServiceSupervisorLiveness.ViaStateFile, liveness.SeenVia);
        Assert.Equal(
            "it was last written 5m ago, and the mutex is not visible from this session",
            liveness.Detail
        );
    }

    [Fact]
    public void ReusedPid_IsNotRunning()
    {
        ServiceSupervisorLiveness liveness = ServiceSupervisorFile.FromStateFile(
            State(updatedAgo: TimeSpan.FromSeconds(12)),
            Live(SupervisorPid, Now - TimeSpan.FromSeconds(5)),
            Now,
            FreshFor
        );

        Assert.False(liveness.Running);
        Assert.StartsWith(
            $"pid {SupervisorPid} is another process now (started ",
            liveness.Detail,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void PidGone_IsNotRunning()
    {
        ServiceSupervisorLiveness liveness = ServiceSupervisorFile.FromStateFile(
            State(updatedAgo: TimeSpan.FromSeconds(12)),
            _ => null,
            Now,
            FreshFor
        );

        Assert.False(liveness.Running);
        Assert.Equal($"no process has pid {SupervisorPid}", liveness.Detail);
    }

    [Fact]
    public void StartTimeThatCannotBeRead_IsNotRunning()
    {
        ServiceSupervisorLiveness liveness = ServiceSupervisorFile.FromStateFile(
            State(updatedAgo: TimeSpan.FromSeconds(12)),
            pid => new ProcessTableEntry(pid, 1, "heroesreplay.exe", null, null),
            Now,
            FreshFor
        );

        Assert.False(liveness.Running);
        Assert.Contains("cannot be read from this session", liveness.Detail);
    }

    [Fact]
    public void FileFromAnOlderBuild_MatchesThePidAgainstWhenSupervisionBegan()
    {
        ServiceSupervisorState old = State(updatedAgo: TimeSpan.FromSeconds(12));
        old.ProcessStartedAt = null;
        old.StartedAt = ProcessStart + TimeSpan.FromSeconds(40);

        Assert.True(
            ServiceSupervisorFile
                .FromStateFile(old, Live(SupervisorPid, ProcessStart), Now, FreshFor)
                .Running
        );
        // A pid reused after the supervisor exited started after supervision began.
        Assert.False(
            ServiceSupervisorFile
                .FromStateFile(
                    old,
                    Live(SupervisorPid, Now - TimeSpan.FromSeconds(5)),
                    Now,
                    FreshFor
                )
                .Running
        );
    }

    [Fact]
    public void OlderFileWithoutProcessStartedAt_StillLoads()
    {
        string path = TempPath();
        try
        {
            File.WriteAllText(
                path,
                """{ "pid": 14420, "startedAt": "2026-10-08T08:26:40+00:00", "updatedAt": "2026-10-08T11:25:48+00:00", "budget": 5 }"""
            );

            ServiceSupervisorState state = ServiceSupervisorFile.TryLoad(path);

            Assert.Equal(SupervisorPid, state.Pid);
            Assert.Null(state.ProcessStartedAt);
            Assert.True(
                ServiceSupervisorFile
                    .FromStateFile(state, Live(SupervisorPid, ProcessStart), Now, FreshFor)
                    .Running
            );
        }
        finally
        {
            ServiceSupervisorFile.Delete(path);
        }
    }

    [Fact]
    public void NoFile_IsNotRunningAndNamesNoWay()
    {
        ServiceSupervisorLiveness liveness = ServiceSupervisorFile.FromStateFile(
            null,
            Live(SupervisorPid, ProcessStart),
            Now,
            FreshFor
        );

        Assert.Same(ServiceSupervisorLiveness.None, liveness);
        Assert.False(liveness.Running);
        Assert.Null(liveness.SeenVia);
    }

    [Fact]
    public void Check_WithoutTheMutex_ReadsTheFileAgainstTheRealProcessTable()
    {
        // This test process stands in for the supervisor in another logon session.
        string mutex = @"Local\HeroesReplay.ServiceSupervisor.Test." + Guid.NewGuid().ToString("N");
        string path = TempPath();
        int pid = Environment.ProcessId;
        DateTimeOffset started = ProcessTable.Find(pid).StartTime.Value;
        try
        {
            Save(path, pid, started, DateTimeOffset.UtcNow);
            ServiceSupervisorLiveness fresh = ServiceSupervisorFile.Check(mutex, path);
            Assert.True(fresh.Running);
            Assert.Equal(ServiceSupervisorLiveness.ViaStateFile, fresh.SeenVia);
            Assert.True(ServiceSupervisorFile.IsRunning(mutex, path));

            Save(path, pid, started, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));
            Assert.False(ServiceSupervisorFile.IsRunning(mutex, path));

            Save(path, pid, started - TimeSpan.FromHours(1), DateTimeOffset.UtcNow);
            ServiceSupervisorLiveness reused = ServiceSupervisorFile.Check(mutex, path);
            Assert.False(reused.Running);
            Assert.Contains("is another process now", reused.Detail);
        }
        finally
        {
            ServiceSupervisorFile.Delete(path);
        }
    }

    [Fact]
    public void Supervisor_RecordsItsProcessStartTime_SoAnotherSessionSeesIt()
    {
        string state = TempPath();
        string lockPath = TempPath();
        string mutex = @"Local\HeroesReplay.ServiceSupervisor.Test." + Guid.NewGuid().ToString("N");
        try
        {
            ServiceLockStore.Save(
                lockPath,
                new ServiceLock
                {
                    StartedAt = DateTimeOffset.UtcNow,
                    Processes = new List<ServiceProcessRecord>
                    {
                        new() { Name = "download", Pid = 100 },
                    },
                }
            );
            var supervision = new ServiceSupervision
            {
                LockPath = lockPath,
                StatePath = state,
                ProcessNameOrNull = _ => "heroesreplay",
            };

            Assert.True(supervision.Begin());

            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(state));
            DateTimeOffset recorded = json
                .RootElement.GetProperty("processStartedAt")
                .GetDateTimeOffset();
            Assert.Equal(ProcessTable.Find(Environment.ProcessId).StartTime, recorded);
            Assert.True(ServiceSupervisorFile.IsRunning(mutex, state));
        }
        finally
        {
            ServiceSupervisorFile.Delete(state);
            ServiceLockStore.Delete(lockPath);
        }
    }

    [Fact]
    public void Status_SaysHowItDecided()
    {
        string lockPath = TempPath();
        try
        {
            string running = StatusText(lockPath, ServiceSupervisorLiveness.ByStateFile);
            Assert.Contains(
                $"Supervisor: running (pid {SupervisorPid}, seen via supervisor.json; mutex not visible from this session), roles download;",
                running
            );

            string mutex = StatusText(lockPath, ServiceSupervisorLiveness.ByMutex);
            Assert.Contains(
                $"Supervisor: running (pid {SupervisorPid}, seen via its mutex), roles download;",
                mutex
            );

            string gone = StatusText(
                lockPath,
                ServiceSupervisorLiveness.NotRunning($"no process has pid {SupervisorPid}")
            );
            Assert.Contains(
                $"Supervisor: not running (pid {SupervisorPid} left supervisor.json; no process has pid {SupervisorPid}).",
                gone
            );

            var json = new StringWriter();
            ServiceSupervisor.Status(
                lockPath,
                pid => null,
                spectator: null,
                query: new ServiceStatusQuery
                {
                    Output = ServiceStatusOutput.Json,
                    Out = json,
                    ReadSupervisor = () => State(updatedAgo: TimeSpan.FromSeconds(12)),
                    SupervisorLiveness = () => ServiceSupervisorLiveness.ByStateFile,
                }
            );
            using JsonDocument document = JsonDocument.Parse(json.ToString());
            JsonElement supervisor = document.RootElement.GetProperty("supervisor");
            Assert.True(supervisor.GetProperty("running").GetBoolean());
            Assert.Equal("supervisor.json", supervisor.GetProperty("seenVia").GetString());
            Assert.Equal(
                "mutex not visible from this session",
                supervisor.GetProperty("detail").GetString()
            );
        }
        finally
        {
            ServiceLockStore.Delete(lockPath);
        }
    }

    private static string StatusText(string lockPath, ServiceSupervisorLiveness liveness)
    {
        var text = new StringWriter();
        ServiceSupervisor.Status(
            lockPath,
            pid => null,
            spectator: null,
            query: new ServiceStatusQuery
            {
                Out = text,
                ReadSupervisor = () => State(updatedAgo: TimeSpan.FromSeconds(12)),
                SupervisorLiveness = () => liveness,
            }
        );
        return text.ToString();
    }

    private static ServiceSupervisorState State(TimeSpan updatedAgo) =>
        new()
        {
            Pid = SupervisorPid,
            ProcessStartedAt = ProcessStart,
            StartedAt = ProcessStart + TimeSpan.FromSeconds(40),
            UpdatedAt = Now - updatedAgo,
            Budget = 5,
            BudgetWindowSeconds = 1800,
            StaleRestartAfterSeconds = 120,
            BackoffSeconds = new List<long> { 10, 30, 120, 300 },
            Supervised = new List<string> { "download" },
        };

    private static Func<int, ProcessTableEntry> Live(int pid, DateTimeOffset started) =>
        candidate =>
            candidate == pid
                ? new ProcessTableEntry(pid, 1, "heroesreplay.exe", null, started)
                : null;

    private static void Save(string path, int pid, DateTimeOffset started, DateTimeOffset updated)
    {
        ServiceSupervisorFile.Save(
            path,
            new ServiceSupervisorState
            {
                Pid = pid,
                ProcessStartedAt = started,
                StartedAt = started + TimeSpan.FromSeconds(1),
                UpdatedAt = updated,
            }
        );
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"heroesreplay-supervisor-{Guid.NewGuid():N}.json");
}
