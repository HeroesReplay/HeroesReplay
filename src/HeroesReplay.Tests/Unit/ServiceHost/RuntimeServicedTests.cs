using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// #450: on 2026-10-15 a .NET update deleted the 10.0.12 runtime folder that the stream PC's
/// supervisor and four roles had loaded, and every runtime library they loaded after that failed.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class RuntimeServicedTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ThisProcessesRuntimeFolder_IsWatchedAndPresent()
    {
        RuntimeFolder folder = RuntimeFolder.ForThisProcess();

        Assert.True(folder.Watched);
        Assert.Equal(
            RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar),
            folder.Folder
        );
        Assert.Null(folder.Missing());
    }

    [Fact]
    public void AFolderThatIsDeleted_IsMissingForGood_EvenIfItComesBack()
    {
        string root = TempFolder();
        try
        {
            var folder = new RuntimeFolder(root + Path.DirectorySeparatorChar);
            Assert.Equal(root, folder.Folder);
            Assert.Null(folder.Missing());

            Directory.Delete(root, recursive: true);
            string missing = folder.Missing();
            Assert.Contains(root, missing, StringComparison.Ordinal);
            Assert.Contains("is gone", missing, StringComparison.Ordinal);

            Directory.CreateDirectory(root);
            Assert.Equal(missing, folder.Missing());
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AMarkerFileThatGoes_IsMissing_AndOneThatWasNeverThereIsNot()
    {
        string root = TempFolder();
        try
        {
            string native = Path.Combine(root, "System.IO.Compression.Native.dll");
            File.WriteAllText(native, "zlib");
            var folder = new RuntimeFolder(root);
            Assert.Null(folder.Missing());

            // Markers the folder never had (CoreLib, the deps file) are no sign of servicing,
            // and neither is a file that is not a marker.
            File.WriteAllText(Path.Combine(root, "unrelated.dll"), "x");
            File.Delete(Path.Combine(root, "unrelated.dll"));
            Assert.Null(folder.Missing());

            File.Delete(native);
            Assert.Contains(native, folder.Missing(), StringComparison.Ordinal);
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void AFolderMissingWhenTheProcessStarted_IsNeverReported()
    {
        // A role started after a proof deleted its fake folder must not restart again.
        string root = Path.Combine(
            Path.GetTempPath(),
            "hr-runtime-" + Guid.NewGuid().ToString("N")
        );
        var folder = new RuntimeFolder(root);
        Assert.False(folder.Watched);
        Assert.Null(folder.Missing());
        Assert.Null(new RuntimeFolder(null).Missing());
        Assert.Null(new RuntimeFolder(" ").Missing());
    }

    [Fact]
    public void Heartbeat_ReportsTheServicedRuntime_AndStatusCallsItDegraded()
    {
        string root = TempFolder();
        string runtime = TempFolder();
        var clock = new FakeClock(Now);
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                Identity("download", "rt1"),
                TimeSpan.FromHours(1),
                clock,
                root,
                new RuntimeFolder(runtime)
            );
            heartbeat.Start(CancellationToken.None);
            Assert.Null(Read("download", "rt1", root).Runtime);

            Directory.Delete(runtime, recursive: true);
            clock.Now = Now.AddSeconds(15);
            heartbeat.Beat();

            ServiceReadyReport read = Read("download", "rt1", root);
            Assert.Equal(ServiceHealthCodes.RuntimeServiced, read.Runtime.Code);
            Assert.Equal(runtime, read.Runtime.Folder);
            Assert.Equal(Now.AddSeconds(15), read.Runtime.Since);
            Assert.Contains(runtime, read.Runtime.Cause, StringComparison.Ordinal);

            // The first cause stays, later beats keep the time it was first seen.
            clock.Now = Now.AddMinutes(5);
            heartbeat.RuntimeServiced("A .NET runtime library did not load.");
            heartbeat.Beat();
            Assert.Equal(Now.AddSeconds(15), Read("download", "rt1", root).Runtime.Since);

            ServiceRoleHealth health = Classify(
                "download",
                running: true,
                Read("download", "rt1", root),
                at: Now.AddMinutes(5)
            );
            Assert.Equal(ServiceRoleState.Degraded, health.State);
            Assert.Equal(ServiceHealthCodes.RuntimeServiced, health.CauseCode);
            Assert.Contains("For 4m 45s", health.Cause, StringComparison.Ordinal);
            Assert.Contains(
                "restarts download at once",
                health.Remediation,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "services start --supervise",
                health.Remediation,
                StringComparison.Ordinal
            );
        }
        finally
        {
            Delete(root);
            Delete(runtime);
        }
    }

    [Fact]
    public void AStagingFailureThatCouldNotLoadZlib_IsReportedLikeAMissingFolder()
    {
        string root = TempFolder();
        try
        {
            using var heartbeat = new ServiceHeartbeat(
                Identity("spectate", "rt2"),
                TimeSpan.FromHours(1),
                new FakeClock(Now),
                root
            );
            heartbeat.Start(CancellationToken.None);
            var failure = new IOException(
                "The underlying compression routine could not be loaded correctly.",
                new DllNotFoundException("Unable to load DLL 'System.IO.Compression.Native'")
            );

            Assert.True(
                HeroesReplay.Core.SelfUpdate.ReleaseUnzip.ReportServiced(
                    failure,
                    heartbeat.RuntimeServiced
                )
            );

            ServiceRoleRuntime runtime = heartbeat.CheckRuntime();
            Assert.Equal(ServiceHealthCodes.RuntimeServiced, runtime.Code);
            Assert.Contains("DllNotFoundException", runtime.Cause, StringComparison.Ordinal);
            Assert.Contains(
                "System.IO.Compression.Native",
                runtime.Cause,
                StringComparison.Ordinal
            );
            heartbeat.Beat();
            Assert.Equal(runtime.Cause, Read("spectate", "rt2", root).Runtime.Cause);

            // Any other staging failure is not a serviced runtime.
            Assert.False(
                HeroesReplay.Core.SelfUpdate.ReleaseUnzip.ReportServiced(
                    new IOException("There is not enough space on the disk."),
                    _ => Assert.Fail("not serviced")
                )
            );
        }
        finally
        {
            Delete(root);
        }
    }

    [Fact]
    public void ServicedSpectate_IsLeftToItsReplay_TheOtherRolesAreKilled()
    {
        foreach (string role in new[] { "twitch", "download", "youtube" })
        {
            var ledger = new ServiceRoleRestarts { Role = role };
            Assert.Equal(ServiceRestartAction.Kill, Decide(Serviced(role), ledger, Now));
            Assert.Equal(ServiceRestartPolicy.RuntimeServicedReason, ledger.DownReason);

            // The kill leaves it failed: no backoff.
            ServiceRoleHealth killed = new() { Role = role, State = ServiceRoleState.Failed };
            Assert.Equal(ServiceRestartAction.Scheduled, Decide(killed, ledger, Now.AddSeconds(1)));
            Assert.Equal(Now.AddSeconds(1), ledger.NextRestartAt);
            Assert.Equal(ServiceRestartAction.Restart, Decide(killed, ledger, Now.AddSeconds(1)));
            ServiceRestartPolicy.Restarted(ledger, Now.AddSeconds(1), "n2", failure: null);
            Assert.Equal(ServiceRestartPolicy.RuntimeServicedReason, ledger.LastReason);
        }

        var spectate = new ServiceRoleRestarts { Role = "spectate" };
        Assert.Equal(ServiceRestartAction.None, Decide(Serviced("spectate"), spectate, Now));
        Assert.Null(spectate.DownReason);
    }

    [Fact]
    public void ServicedRole_WithNoBudgetLeft_IsLeftUpAndDegraded()
    {
        var ledger = new ServiceRoleRestarts { Role = "download" };
        for (int i = 0; i < new ServiceRestartSettings().Limit; i++)
        {
            ledger.Recent.Add(Now.AddMinutes(-i));
        }

        Assert.Equal(ServiceRestartAction.None, Decide(Serviced("download"), ledger, Now));
        Assert.False(ledger.Exhausted);
    }

    [Fact]
    public void SpectateThatExitedOffAServicedRuntime_IsFailedAndRestartsAtOnce()
    {
        ServiceReadyReport beat = Beat("spectate");
        beat.Readiness = ServiceReadiness.Exited;
        beat.Runtime = Runtime();

        ServiceRoleHealth health = Classify("spectate", running: false, beat);

        Assert.Equal(ServiceRoleState.Failed, health.State);
        Assert.Equal(ServiceHealthCodes.RuntimeServiced, health.CauseCode);
        Assert.Contains(
            "exited to restart on the current .NET runtime",
            health.Cause,
            StringComparison.Ordinal
        );
        var ledger = new ServiceRoleRestarts { Role = "spectate" };
        Assert.Equal(ServiceRestartAction.Scheduled, Decide(health, ledger, Now));
        Assert.Equal(Now, ledger.NextRestartAt);
        Assert.Equal(ServiceRestartPolicy.RuntimeServicedReason, ledger.DownReason);

        // A plain exit without the runtime keeps its backoff.
        beat.Runtime = null;
        var plain = new ServiceRoleRestarts { Role = "spectate" };
        Assert.Equal(
            ServiceRestartAction.Scheduled,
            Decide(Classify("spectate", running: false, beat), plain, Now)
        );
        Assert.Equal(Now.AddSeconds(10), plain.NextRestartAt);
    }

    [Fact]
    public void ServicedSpectate_TellsTheOperatorItStopsAfterThisReplay()
    {
        ServiceReadyReport beat = Beat("spectate");
        beat.Runtime = Runtime();
        beat.StreamHoldSince = Now.AddMinutes(-3);

        ServiceRoleHealth health = Classify("spectate", running: true, beat);

        // The serviced runtime wins over a stream hold, so status does not hide it.
        Assert.Equal(ServiceRoleState.Degraded, health.State);
        Assert.Equal(ServiceHealthCodes.RuntimeServiced, health.CauseCode);
        Assert.Contains("stops after this replay", health.Remediation, StringComparison.Ordinal);
    }

    [Fact]
    public void AStalledLaunch_StillWinsOverAServicedRuntime_SoTheSupervisorKillsIt()
    {
        ServiceReadyReport beat = Beat("spectate");
        beat.Runtime = Runtime();
        beat.LaunchingSince = Now.AddMinutes(-21);
        beat.LastSuccessfulWorkAt = Now.AddMinutes(-30);

        ServiceRoleHealth health = Classify("spectate", running: true, beat);

        Assert.Equal(ServiceHealthCodes.SpectateLaunchStalled, health.CauseCode);
        Assert.Equal(
            ServiceRestartAction.Kill,
            Decide(health, new ServiceRoleRestarts { Role = "spectate" }, Now)
        );
    }

    [Fact]
    public void AStoppingRole_IsNotReportedServiced()
    {
        ServiceReadyReport beat = Beat("youtube");
        beat.Runtime = Runtime();
        beat.Readiness = ServiceReadiness.Stopping;

        Assert.NotEqual(
            ServiceHealthCodes.RuntimeServiced,
            Classify("youtube", running: true, beat).CauseCode
        );
    }

    [Fact]
    public void SpectateRestart_IsDueOnlyWhenServicedAndSupervised_AndWarnsOnceWithout()
    {
        ServiceRoleRuntime runtime = null;
        bool supervised = false;
        var log = new ListLogger();
        var restart = new RuntimeRestart(log, () => runtime, () => supervised);

        Assert.False(restart.Due());
        Assert.False(restart.HandOff());

        runtime = Runtime();
        Assert.False(restart.Due());
        Assert.False(restart.Due());
        Assert.False(restart.HandOff());
        string warning = Assert.Single(log.Messages);
        Assert.Contains("No supervisor runs", warning, StringComparison.Ordinal);

        supervised = true;
        Assert.True(restart.Due());
        Assert.True(restart.HandOff());
        Assert.Contains(
            ServiceHealthCodes.RuntimeServiced,
            log.Messages[^1],
            StringComparison.Ordinal
        );
        Assert.Contains("waiting scene", log.Messages[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void ARuntimeThatIsThere_IsNoRestart_EvenWithASupervisor()
    {
        Assert.False(new RuntimeRestart(NullLogger.Instance, () => null, () => true).Due());
    }

    [Fact]
    public void Successor_IsSuperviseAfterThisPid_InAConsoleOfItsOwn()
    {
        ProcessStartInfo start = ServiceSupervisorHandOver.Successor(
            @"C:\heroesreplay\app\heroesreplay.exe",
            4242
        );

        Assert.Equal(@"C:\heroesreplay\app\heroesreplay.exe", start.FileName);
        Assert.Equal("services supervise --after-pid 4242", start.Arguments);
        Assert.Equal(@"C:\heroesreplay\app", start.WorkingDirectory);
        Assert.True(start.UseShellExecute);
    }

    [Fact]
    public void StartSuccessor_LogsTheNewPid_OrTheFailureSoThisSupervisorKeepsGoing()
    {
        var log = new ListLogger();
        ProcessStartInfo seen = null;

        Assert.True(
            ServiceSupervisorHandOver.StartSuccessor(
                @"C:\app\heroesreplay.exe",
                log,
                start =>
                {
                    seen = start;
                    return 77;
                }
            )
        );
        Assert.Equal($"services supervise --after-pid {Environment.ProcessId}", seen.Arguments);
        Assert.Contains("pid 77", log.Messages[^1], StringComparison.Ordinal);

        Assert.False(
            ServiceSupervisorHandOver.StartSuccessor(@"C:\app\heroesreplay.exe", log, _ => null)
        );
        Assert.False(
            ServiceSupervisorHandOver.StartSuccessor(
                @"C:\app\heroesreplay.exe",
                log,
                _ => throw new InvalidOperationException("no console")
            )
        );
        Assert.Equal(2, log.Levels.Count(level => level == LogLevel.Error));
        Assert.Contains("keeps supervising", log.Messages[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewSupervisor_WaitsForTheOldOneToExit_Bounded()
    {
        int polls = 0;
        Assert.True(
            ServiceSupervisorHandOver.WaitForPredecessor(
                4242,
                TimeSpan.FromSeconds(60),
                _ => ++polls < 4,
                _ => { }
            )
        );
        Assert.Equal(4, polls);

        var waits = new List<TimeSpan>();
        Assert.False(
            ServiceSupervisorHandOver.WaitForPredecessor(
                4242,
                TimeSpan.FromSeconds(1),
                _ => true,
                waits.Add
            )
        );
        Assert.Equal(TimeSpan.FromSeconds(1), TimeSpan.FromTicks(waits.Sum(wait => wait.Ticks)));
        Assert.True(
            ServiceSupervisorHandOver.WaitForPredecessor(
                0,
                TimeSpan.FromSeconds(1),
                _ => true,
                _ => { }
            )
        );
    }

    private static ServiceRoleHealth Serviced(string role) =>
        new()
        {
            Role = role,
            State = ServiceRoleState.Degraded,
            CauseCode = ServiceHealthCodes.RuntimeServiced,
            Cause = "It runs on a .NET runtime that a .NET update serviced under it.",
        };

    private static ServiceRoleRuntime Runtime() =>
        new()
        {
            Code = ServiceHealthCodes.RuntimeServiced,
            Folder = @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.12",
            Cause =
                @"The .NET runtime folder C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.12 that this process loaded is gone: a .NET update serviced it.",
            Since = Now.AddMinutes(-2),
        };

    private static ServiceReadyReport Beat(string role) =>
        new()
        {
            Role = role,
            Version = "1.0.0-955",
            Readiness = ServiceReadiness.Ready,
            ReadyAt = Now.AddDays(-3),
            HeartbeatAt = Now.AddSeconds(-5),
            HeartbeatIntervalSeconds = 15,
            LastSuccessfulWorkAt = Now.AddMinutes(-1),
        };

    private static ServiceRestartAction Decide(
        ServiceRoleHealth health,
        ServiceRoleRestarts ledger,
        DateTimeOffset at
    ) => ServiceRestartPolicy.Decide(health, ledger, at, new ServiceRestartSettings());

    private static ServiceRoleHealth Classify(
        string role,
        bool running,
        ServiceReadyReport heartbeat,
        DateTimeOffset? at = null
    ) =>
        ServiceHealthClassifier.Classify(
            role,
            new ServiceProcessRecord
            {
                Name = role,
                Pid = 70,
                Nonce = "nonce",
                Arguments = role,
            },
            running,
            heartbeat,
            stopRequested: false,
            at ?? Now,
            new ServiceHealthSettings()
        );

    private static ServiceReadyReport Identity(string role, string nonce) =>
        new()
        {
            Role = role,
            Nonce = nonce,
            Version = "9.9.9",
            ExecutablePath = @"C:\heroesreplay\app\heroesreplay.exe",
            Pid = 4242,
        };

    private static ServiceReadyReport Read(string role, string nonce, string root) =>
        ServiceReadyFile.TryRead(new ServiceProcessRecord { Name = role, Nonce = nonce }, root);

    private static string TempFolder()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "heroesreplay-runtime-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
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

    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public List<LogLevel> Levels { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            Levels.Add(logLevel);
            Messages.Add(formatter(state, exception));
        }
    }
}
