using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.Core.ServiceHost;
using HeroesReplay.Core.Status;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceHealthClassifierTests
{
    private const string Exe = @"C:\heroesreplay\app\heroesreplay.exe";
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ServiceHealthSettings Defaults = new();

    [Fact]
    public void NotInTheLock_IsStopped()
    {
        ServiceRoleHealth health = ServiceHealthClassifier.Classify(
            "twitch",
            record: null,
            running: false,
            heartbeat: null,
            stopRequested: false,
            Now,
            Defaults
        );

        Assert.Equal(ServiceRoleState.Stopped, health.State);
        Assert.Equal("service.stopped", health.Code);
        Assert.False(health.Expected);
        Assert.Contains("services start", health.Remediation);
    }

    [Fact]
    public void FreshHeartbeatAndRecentWork_IsReady()
    {
        ServiceRoleHealth health = Classify(
            "download",
            running: true,
            Beat(heartbeatAgo: TimeSpan.FromSeconds(10), workAgo: TimeSpan.FromSeconds(20))
        );

        Assert.Equal(ServiceRoleState.Ready, health.State);
        Assert.Equal("service.ready", health.Code);
        Assert.Null(health.Remediation);
        Assert.Equal(10, health.HeartbeatAgeSeconds);
        Assert.Equal(20, health.WorkAgeSeconds);
        Assert.Equal(45, health.StaleAfterSeconds);
        Assert.Equal(900, health.WorkThresholdSeconds);
        Assert.Equal(Exe, health.ExecutablePath);
        Assert.Equal("1.2.3", health.Version);
    }

    [Fact]
    public void NoWorkYetInsideTheThreshold_IsReady()
    {
        ServiceReadyReport beat = Beat(heartbeatAgo: TimeSpan.FromSeconds(5), workAgo: null);
        beat.ReadyAt = Now.AddMinutes(-2);

        ServiceRoleHealth health = Classify("download", running: true, beat);

        Assert.Equal(ServiceRoleState.Ready, health.State);
        Assert.Contains("no download pass yet", health.Cause);
    }

    [Theory]
    [InlineData(45, ServiceRoleState.Ready)]
    [InlineData(46, ServiceRoleState.Stale)]
    [InlineData(600, ServiceRoleState.Stale)]
    public void HeartbeatOlderThanThreeIntervals_IsStale(int seconds, ServiceRoleState expected)
    {
        ServiceRoleHealth health = Classify(
            "spectate",
            running: true,
            Beat(heartbeatAgo: TimeSpan.FromSeconds(seconds), workAgo: TimeSpan.FromSeconds(1))
        );

        Assert.Equal(expected, health.State);
        if (expected == ServiceRoleState.Stale)
        {
            Assert.Equal("service.stale", health.Code);
            Assert.Contains("limit 45s", health.Cause);
            Assert.Contains("hung or suspended", health.Remediation);
        }
    }

    [Theory]
    [InlineData(2, ServiceRoleState.Ready)]
    [InlineData(3, ServiceRoleState.Degraded)]
    [InlineData(7, ServiceRoleState.Degraded)]
    public void SpectateSessionsWithoutMatchProgress_DegradeAtTheLimit(
        int sessions,
        ServiceRoleState expected
    )
    {
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(3),
            workAgo: TimeSpan.FromMinutes(1)
        );
        beat.SessionsWithoutProgress = sessions;
        beat.LastOutcome = "ClientCrashed";

        ServiceRoleHealth health = Classify("spectate", running: true, beat);

        Assert.Equal(expected, health.State);
        Assert.Equal(sessions, health.SessionsWithoutProgress);
        Assert.Equal("ClientCrashed", health.LastOutcome);
        if (expected == ServiceRoleState.Degraded)
        {
            Assert.Equal("service.degraded", health.Code);
            Assert.Equal(ServiceHealthCodes.SpectateNoMatchProgress, health.CauseCode);
            Assert.Contains($"{sessions} replay sessions in a row", health.Cause);
            Assert.Contains("ended ClientCrashed", health.Cause);
            Assert.Contains("load timeout", health.Remediation);
        }
        else
        {
            Assert.Null(health.CauseCode);
        }
    }

    [Fact]
    public void SpectateNoProgressSessions_IsConfigurable_AndOnlyForSpectate()
    {
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(3),
            workAgo: TimeSpan.FromMinutes(1)
        );
        beat.SessionsWithoutProgress = 5;
        var settings = new ServiceHealthSettings { SpectateNoProgressSessions = 6 };

        ServiceRoleHealth spectate = ServiceHealthClassifier.Classify(
            "spectate",
            Record("spectate", 70),
            running: true,
            beat,
            stopRequested: false,
            Now,
            settings
        );
        ServiceRoleHealth download = Classify("download", running: true, beat);

        Assert.Equal(ServiceRoleState.Ready, spectate.State);
        Assert.Equal(ServiceRoleState.Ready, download.State);
        Assert.Equal(3, Defaults.NoProgressSessions("spectate"));
        Assert.Equal(0, Defaults.NoProgressSessions("download"));
    }

    [Fact]
    public void StaleUsesTheIntervalTheRoleWrote()
    {
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(20),
            workAgo: TimeSpan.FromSeconds(1)
        );
        beat.HeartbeatIntervalSeconds = 5;

        ServiceRoleHealth health = Classify("spectate", running: true, beat);

        Assert.Equal(ServiceRoleState.Stale, health.State);
        Assert.Equal(15, health.StaleAfterSeconds);
    }

    [Fact]
    public void LiveProcessWithoutAHeartbeatFile_IsStale()
    {
        ServiceRoleHealth health = Classify("youtube", running: true, heartbeat: null);

        Assert.Equal(ServiceRoleState.Stale, health.State);
        Assert.Contains("no heartbeat file", health.Cause);
    }

    [Fact]
    public void WorkOlderThanTheRoleThreshold_IsDegraded_PerRole()
    {
        // Six minutes is past Twitch's five-minute limit and inside download's fifteen.
        ServiceRoleHealth twitch = Classify(
            "twitch",
            running: true,
            Beat(heartbeatAgo: TimeSpan.FromSeconds(3), workAgo: TimeSpan.FromMinutes(6))
        );
        ServiceRoleHealth download = Classify(
            "download",
            running: true,
            Beat(heartbeatAgo: TimeSpan.FromSeconds(3), workAgo: TimeSpan.FromMinutes(6))
        );

        Assert.Equal(ServiceRoleState.Degraded, twitch.State);
        Assert.Equal("service.degraded", twitch.Code);
        Assert.Contains("Last successful Twitch reconcile was 6m ago (limit 5m)", twitch.Cause);
        Assert.Equal(ServiceRoleState.Ready, download.State);
    }

    [Fact]
    public void NoWorkSinceReadyPastTheThreshold_IsDegraded()
    {
        ServiceReadyReport beat = Beat(heartbeatAgo: TimeSpan.FromSeconds(3), workAgo: null);
        beat.ReadyAt = Now.AddMinutes(-31);

        ServiceRoleHealth health = Classify("youtube", running: true, beat);

        Assert.Equal(ServiceRoleState.Degraded, health.State);
        Assert.Contains("No successful upload pass since it became ready 31m ago", health.Cause);
    }

    [Fact]
    public void ErrorNewerThanTheLastWork_IsDegraded()
    {
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(3),
            workAgo: TimeSpan.FromSeconds(40)
        );
        beat.LastError = new ServiceRoleError
        {
            Message = "Heroes Profile download failed.",
            At = Now.AddSeconds(-10),
        };

        ServiceRoleHealth health = Classify("download", running: true, beat);

        Assert.Equal(ServiceRoleState.Degraded, health.State);
        Assert.Contains("Last error 10s ago: Heroes Profile download failed.", health.Cause);
        Assert.Equal("Heroes Profile download failed.", health.LastError.Message);
    }

    [Fact]
    public void ErrorOlderThanTheLastWork_IsReady()
    {
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(3),
            workAgo: TimeSpan.FromSeconds(5)
        );
        beat.LastError = new ServiceRoleError { Message = "old", At = Now.AddMinutes(-1) };

        ServiceRoleHealth health = Classify("download", running: true, beat);

        Assert.Equal(ServiceRoleState.Ready, health.State);
        Assert.Equal("old", health.LastError.Message);
    }

    [Fact]
    public void ConfiguredThresholdsReplaceTheDefaults()
    {
        var settings = new ServiceHealthSettings
        {
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            StaleAfterIntervals = 2,
            DownloadWorkThreshold = TimeSpan.FromSeconds(30),
        };
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(8),
            workAgo: TimeSpan.FromSeconds(40)
        );
        beat.HeartbeatIntervalSeconds = null;

        ServiceRoleHealth health = ServiceHealthClassifier.Classify(
            "download",
            Record("download", 70),
            running: true,
            beat,
            stopRequested: false,
            Now,
            settings
        );

        Assert.Equal(ServiceRoleState.Degraded, health.State);
        Assert.Equal(10, health.StaleAfterSeconds);
        Assert.Equal(30, health.WorkThresholdSeconds);
    }

    [Fact]
    public void ExitedAfterAStopRequest_IsStopped()
    {
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(30),
            workAgo: TimeSpan.FromSeconds(31)
        );
        beat.Readiness = ServiceReadiness.Stopping;

        ServiceRoleHealth byHeartbeat = Classify("download", running: false, beat);
        ServiceRoleHealth byStopFile = ServiceHealthClassifier.Classify(
            "download",
            Record("download", 70),
            running: false,
            Beat(heartbeatAgo: TimeSpan.FromSeconds(30), workAgo: null),
            stopRequested: true,
            Now,
            Defaults
        );

        Assert.Equal(ServiceRoleState.Stopped, byHeartbeat.State);
        Assert.Contains("exited after a stop request", byHeartbeat.Cause);
        Assert.True(byHeartbeat.Expected);
        Assert.Equal(ServiceRoleState.Stopped, byStopFile.State);
    }

    [Fact]
    public void ExitedWithoutAStopRequest_IsFailed()
    {
        ServiceReadyReport crashed = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(50),
            workAgo: TimeSpan.FromMinutes(1)
        );
        crashed.LastError = new ServiceRoleError
        {
            Message = "An unexpected error in the replay engine.",
            At = Now.AddSeconds(-55),
        };
        ServiceReadyReport leftLoop = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(5),
            workAgo: TimeSpan.FromSeconds(6)
        );
        leftLoop.Readiness = ServiceReadiness.Exited;

        ServiceRoleHealth crash = Classify("spectate", running: false, crashed);
        ServiceRoleHealth exit = Classify("spectate", running: false, leftLoop);
        ServiceRoleHealth gone = Classify("spectate", running: false, heartbeat: null);

        Assert.Equal(ServiceRoleState.Failed, crash.State);
        Assert.Equal("service.failed", crash.Code);
        Assert.Contains("exited unexpectedly", crash.Cause);
        Assert.Contains("An unexpected error in the replay engine.", crash.Cause);
        Assert.Contains("services stop", crash.Remediation);
        Assert.Equal(ServiceRoleState.Failed, exit.State);
        Assert.Contains("without a stop request", exit.Cause);
        Assert.Equal(ServiceRoleState.Failed, gone.State);
    }

    [Fact]
    public void Build_ClassifiesEveryRoleFromTheFakeProcessTable()
    {
        var processes = new Dictionary<int, string>
        {
            [10] = "heroesreplay",
            [11] = "heroesreplay",
            [13] = "heroesreplay",
        };
        var heartbeats = new Dictionary<string, ServiceReadyReport>
        {
            ["n10"] = Beat(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), "spectate", "n10"),
            ["n11"] = Beat(TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3), "twitch", "n11"),
            ["n12"] = Beat(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2), "download", "n12"),
            ["n13"] = Beat(TimeSpan.FromSeconds(5), TimeSpan.FromHours(1), "youtube", "n13"),
        };
        ServiceLock snapshot = Lock(
            Record("spectate", 10, "n10"),
            Record("twitch", 11, "n11"),
            Record("download", 12, "n12"),
            Record("youtube", 13, "n13")
        );

        ServiceStatusReport report = ServiceHealthClassifier.Build(
            snapshot,
            pid => processes.GetValueOrDefault(pid),
            pid => new ServiceProcessProbe { ExecutablePath = Exe },
            record => heartbeats.GetValueOrDefault(record.Nonce),
            stopRequested: false,
            Now,
            Defaults,
            "dev"
        );

        Assert.Equal(
            new[] { "spectate", "twitch", "download", "youtube" },
            report.Roles.Select(role => role.Role).ToArray()
        );
        Assert.Equal(
            new[]
            {
                ServiceRoleState.Ready,
                ServiceRoleState.Stale,
                ServiceRoleState.Failed,
                ServiceRoleState.Degraded,
            },
            report.Roles.Select(role => role.State).ToArray()
        );
        Assert.False(report.Ok);
        Assert.Equal("service.failed", report.Code);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal("dev", report.Environment);
        Assert.Equal(
            "1 of 4 roles ready; download failed; twitch stale; youtube degraded.",
            report.Message
        );
    }

    [Fact]
    public void Build_ReusedPid_CountsAsGone()
    {
        ServiceLock snapshot = Lock(Record("download", 12, "n12"));

        ServiceStatusReport report = ServiceHealthClassifier.Build(
            snapshot,
            pid => "heroesreplay",
            pid => new ServiceProcessProbe { ExecutablePath = @"D:\other\heroesreplay.exe" },
            record => Beat(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), "download", "n12"),
            stopRequested: false,
            Now,
            Defaults
        );

        ServiceRoleHealth download = report.Roles.Single(role => role.Role == "download");
        Assert.False(download.Running);
        Assert.Equal(ServiceRoleState.Failed, download.State);
    }

    [Fact]
    public void Build_NothingRecorded_IsStoppedAndOk()
    {
        ServiceStatusReport report = ServiceHealthClassifier.Build(
            snapshot: null,
            pid => null,
            pid => null,
            record => null,
            stopRequested: false,
            Now,
            Defaults
        );

        Assert.True(report.Ok);
        Assert.Equal("service.stopped", report.Code);
        Assert.Equal(0, report.ExitCode);
        Assert.Equal(4, report.Roles.Count);
        Assert.All(report.Roles, role => Assert.Equal(ServiceRoleState.Stopped, role.State));
        Assert.Equal("No HeroesReplay services are running.", report.Message);
    }

    [Fact]
    public void Build_AllReady_IsOk()
    {
        var records = new[] { "spectate", "twitch", "download", "youtube" }
            .Select((name, index) => Record(name, 20 + index, "r" + index))
            .ToArray();

        ServiceStatusReport report = ServiceHealthClassifier.Build(
            Lock(records),
            pid => "heroesreplay",
            pid => null,
            record =>
                Beat(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), record.Name, record.Nonce),
            stopRequested: false,
            Now,
            Defaults
        );

        Assert.True(report.Ok);
        Assert.Equal("service.ready", report.Code);
        Assert.Equal("4 of 4 roles ready.", report.Message);
    }

    [Fact]
    public void Worst_OrdersFailedStaleDegradedReadyStopped()
    {
        ServiceRoleHealth Role(ServiceRoleState state) => new() { State = state };

        Assert.Equal(
            ServiceRoleState.Stale,
            ServiceHealthClassifier.Worst(
                new[]
                {
                    Role(ServiceRoleState.Ready),
                    Role(ServiceRoleState.Stale),
                    Role(ServiceRoleState.Degraded),
                }
            )
        );
        Assert.Equal(
            ServiceRoleState.Ready,
            ServiceHealthClassifier.Worst(
                new[] { Role(ServiceRoleState.Stopped), Role(ServiceRoleState.Ready) }
            )
        );
        Assert.Equal(
            ServiceRoleState.Stopped,
            ServiceHealthClassifier.Worst(Array.Empty<ServiceRoleHealth>())
        );
    }

    [Fact]
    public void Json_IsTheStableEnvelope()
    {
        ServiceStatusReport report = ServiceHealthClassifier.Build(
            Lock(Record("download", 12, "n12")),
            pid => "heroesreplay",
            pid => null,
            record => Beat(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(9), "download", "n12"),
            stopRequested: false,
            Now,
            Defaults,
            "dev",
            new SpectatorStatus { Phase = "Idle", UpdatedAt = Now }
        );

        using JsonDocument json = JsonDocument.Parse(report.ToJson());
        JsonElement root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("service.ready", root.GetProperty("code").GetString());
        Assert.Equal("dev", root.GetProperty("environment").GetString());
        Assert.False(root.TryGetProperty("exitCode", out _));
        Assert.Equal("Idle", root.GetProperty("spectator").GetProperty("phase").GetString());

        JsonElement[] roles = root.GetProperty("roles").EnumerateArray().ToArray();
        Assert.Equal(4, roles.Length);
        JsonElement download = roles.Single(role =>
            role.GetProperty("role").GetString() == "download"
        );
        Assert.Equal("ready", download.GetProperty("state").GetString());
        Assert.Equal("service.ready", download.GetProperty("code").GetString());
        Assert.Equal(12, download.GetProperty("pid").GetInt32());
        Assert.Equal("n12", download.GetProperty("nonce").GetString());
        Assert.Equal("1.2.3", download.GetProperty("version").GetString());
        Assert.Equal(Exe, download.GetProperty("executablePath").GetString());
        Assert.Equal("ready", download.GetProperty("readiness").GetString());
        Assert.Equal(4, download.GetProperty("heartbeatAgeSeconds").GetInt64());
        Assert.Equal(45, download.GetProperty("staleAfterSeconds").GetInt64());
        Assert.Equal(9, download.GetProperty("workAgeSeconds").GetInt64());
        Assert.Equal(900, download.GetProperty("workThresholdSeconds").GetInt64());
        Assert.Equal(JsonValueKind.Null, download.GetProperty("lastError").ValueKind);
        foreach (
            string key in new[]
            {
                "cause",
                "remediation",
                "expected",
                "running",
                "arguments",
                "startedAt",
                "readyAt",
                "heartbeatAt",
                "lastSuccessfulWorkAt",
            }
        )
        {
            Assert.True(download.TryGetProperty(key, out _), key);
        }

        JsonElement youtube = roles.Single(role =>
            role.GetProperty("role").GetString() == "youtube"
        );
        Assert.Equal("stopped", youtube.GetProperty("state").GetString());
        Assert.Equal("service.stopped", youtube.GetProperty("code").GetString());
        Assert.False(youtube.GetProperty("expected").GetBoolean());

        ServiceStatusReport parsed = ServiceStatusReport.FromJson(report.ToJson());
        Assert.Equal(ServiceRoleState.Ready, parsed.Roles.Single(r => r.Role == "download").State);
    }

    [Fact]
    public void Status_WritesJsonAndExitsOnHealth()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-services-{Guid.NewGuid():N}.json"
        );
        try
        {
            ServiceLockStore.Save(path, Lock(Record("download", 12, "n12")));
            var output = new StringWriter();

            int code = ServiceSupervisor.Status(
                path,
                pid => "heroesreplay",
                spectator: null,
                pid => null,
                new ServiceStatusQuery
                {
                    Output = ServiceStatusOutput.Json,
                    Time = new FixedClock(Now),
                    ReadHeartbeat = record =>
                        Beat(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), "download", "n12"),
                    Out = output,
                }
            );

            Assert.Equal(1, code);
            using JsonDocument json = JsonDocument.Parse(output.ToString());
            Assert.Equal("service.stale", json.RootElement.GetProperty("code").GetString());
            Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Fact]
    public void Status_TextNamesEachRoleItsStateCauseAndFix()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-services-{Guid.NewGuid():N}.json"
        );
        try
        {
            ServiceLockStore.Save(path, Lock(Record("download", 12, "n12")));
            var output = new StringWriter();

            int code = ServiceSupervisor.Status(
                path,
                pid => null,
                spectator: null,
                pid => null,
                new ServiceStatusQuery
                {
                    Time = new FixedClock(Now),
                    ReadHeartbeat = record =>
                        Beat(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), "download", "n12"),
                    Out = output,
                }
            );

            string text = output.ToString();
            Assert.Equal(1, code);
            Assert.Contains("[service.failed]", text);
            Assert.Contains("download failed   pid 12, heartbeat 30s ago", text);
            Assert.Contains("exited unexpectedly", text);
            Assert.Contains("Fix: Check the download console", text);
            Assert.Contains("twitch   stopped  Not running and not expected", text);
            Assert.Contains("Spectator status: no snapshot.", text);
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    [Theory]
    [InlineData(19, ServiceRoleState.Ready)]
    [InlineData(21, ServiceRoleState.Degraded)]
    public void SpectateLaunchWithoutMatchProgress_IsStalledPastTheThreshold(
        int minutes,
        ServiceRoleState expected
    )
    {
        // #249: spectate heartbeated normally while one replay sat in Wait, so status said ready.
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(3),
            workAgo: TimeSpan.FromMinutes(minutes + 1)
        );
        beat.LaunchingSince = Now.AddMinutes(-minutes);
        var settings = new ServiceHealthSettings { SpectateWorkThreshold = TimeSpan.FromHours(2) };

        ServiceRoleHealth health = ServiceHealthClassifier.Classify(
            "spectate",
            Record("spectate", 70),
            running: true,
            beat,
            stopRequested: false,
            Now,
            settings
        );

        Assert.Equal(expected, health.State);
        Assert.Equal(beat.LaunchingSince, health.LaunchingSince);
        if (expected == ServiceRoleState.Degraded)
        {
            Assert.Equal(ServiceHealthCodes.SpectateLaunchStalled, health.CauseCode);
            Assert.Contains("launching or loading for 21m", health.Cause);
            Assert.Contains("limit 20m", health.Cause);
        }
        else
        {
            Assert.Null(health.CauseCode);
        }
    }

    [Fact]
    public void EmptyQueueOutageOrHeldReplays_AreNeverAStalledLaunch()
    {
        // No launch phase: an idle queue, an outage pause, and the wait after a held replay all
        // leave LaunchingSince empty. Old work only makes it degraded, which is not restarted.
        ServiceReadyReport idle = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(3),
            workAgo: TimeSpan.FromHours(3)
        );
        idle.SessionsWithoutProgress = 2;
        idle.LastOutcome = "BuildNotInstalled";

        ServiceRoleHealth health = Classify("spectate", running: true, idle);

        Assert.Equal(ServiceRoleState.Degraded, health.State);
        Assert.Null(health.CauseCode);
        Assert.Null(health.LaunchingSince);
        Assert.Equal(
            ServiceRestartAction.None,
            ServiceRestartPolicy.Decide(
                health,
                new ServiceRoleRestarts { Role = "spectate" },
                Now,
                new ServiceRestartSettings()
            )
        );
    }

    [Fact]
    public void LaunchStall_IsSpectateOnly_AndNotWhileStopping_AndConfigurable()
    {
        ServiceReadyReport beat = Beat(
            heartbeatAgo: TimeSpan.FromSeconds(3),
            workAgo: TimeSpan.FromMinutes(1)
        );
        beat.LaunchingSince = Now.AddMinutes(-30);
        beat.LastSuccessfulWorkAt = Now.AddMinutes(-31);
        var settings = new ServiceHealthSettings
        {
            SpectateLaunchStallThreshold = TimeSpan.FromMinutes(45),
            SpectateWorkThreshold = TimeSpan.FromHours(2),
        };

        ServiceRoleHealth longer = ServiceHealthClassifier.Classify(
            "spectate",
            Record("spectate", 70),
            running: true,
            beat,
            stopRequested: false,
            Now,
            settings
        );
        ServiceRoleHealth download = Classify("download", running: true, beat);
        beat.Readiness = ServiceReadiness.Stopping;
        ServiceRoleHealth stopping = Classify("spectate", running: true, beat);

        Assert.Equal(ServiceRoleState.Ready, longer.State);
        Assert.NotEqual(ServiceHealthCodes.SpectateLaunchStalled, download.CauseCode);
        Assert.NotEqual(ServiceHealthCodes.SpectateLaunchStalled, stopping.CauseCode);
        Assert.Equal(TimeSpan.FromMinutes(20), Defaults.LaunchStallThreshold("spectate"));
        Assert.Equal(TimeSpan.Zero, Defaults.LaunchStallThreshold("youtube"));
    }

    private static ServiceRoleHealth Classify(
        string role,
        bool running,
        ServiceReadyReport heartbeat
    ) =>
        ServiceHealthClassifier.Classify(
            role,
            Record(role, 70),
            running,
            heartbeat,
            stopRequested: false,
            Now,
            Defaults
        );

    private static ServiceReadyReport Beat(
        TimeSpan heartbeatAgo,
        TimeSpan? workAgo,
        string role = null,
        string nonce = null
    ) =>
        new()
        {
            Role = role,
            Nonce = nonce,
            Version = "1.2.3",
            ExecutablePath = Exe,
            Readiness = ServiceReadiness.Ready,
            ReadyAt = Now.AddHours(-2),
            HeartbeatAt = Now - heartbeatAgo,
            HeartbeatIntervalSeconds = 15,
            LastSuccessfulWorkAt = workAgo == null ? null : Now - workAgo.Value,
        };

    private static ServiceProcessRecord Record(string name, int pid, string nonce = "nonce") =>
        new()
        {
            Name = name,
            Pid = pid,
            Nonce = nonce,
            Arguments = name,
            ExecutablePath = Exe,
            Version = "1.2.3",
        };

    private static ServiceLock Lock(params ServiceProcessRecord[] records) =>
        new() { StartedAt = Now.AddHours(-2), Processes = records.ToList() };

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset now;

        public FixedClock(DateTimeOffset now)
        {
            this.now = now;
        }

        public override DateTimeOffset GetUtcNow() => now;
    }
}
