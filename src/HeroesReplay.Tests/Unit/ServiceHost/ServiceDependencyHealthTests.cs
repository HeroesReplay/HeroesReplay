using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HeroesReplay.CLI.Commands.Services;
using HeroesReplay.CLI.Output;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

/// <summary>
/// #305: a role whose own probe finds its dependency rejected or unreachable is degraded with the
/// probe's cause code and fix, never failed, and the supervisor does not restart it.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceDependencyHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(
        "download",
        ServiceDependencyStates.Rejected,
        "download.heroesprofile_rejected",
        "Heroes Profile rejected HeroesProfileApi:ApiKey (HTTP 401)."
    )]
    [InlineData(
        "download",
        ServiceDependencyStates.Unreachable,
        "download.heroesprofile_unreachable",
        "Heroes Profile did not answer within 10s."
    )]
    [InlineData(
        "youtube",
        ServiceDependencyStates.Rejected,
        "youtube.oauth_invalid",
        "Google refused the stored upload consent."
    )]
    [InlineData(
        "spectate",
        ServiceDependencyStates.Unreachable,
        "spectate.obs_unreachable",
        "OBS at ws://127.0.0.1:4455 did not answer within 3 s."
    )]
    [InlineData(
        "twitch",
        ServiceDependencyStates.Rejected,
        "twitch.token_invalid",
        "Twitch rejected Twitch:AccessToken (HTTP 401)."
    )]
    public void AFailedProbe_IsDegradedWithItsCauseCodeAndFix_NotFailed(
        string role,
        string state,
        string code,
        string cause
    )
    {
        ServiceReadyReport beat = Beat(Dependency(state, code, cause, since: Now.AddMinutes(-6)));

        ServiceRoleHealth health = Classify(role, beat);

        Assert.Equal(ServiceRoleState.Degraded, health.State);
        Assert.Equal(ServiceHealthCodes.Degraded, health.Code);
        Assert.Equal(code, health.CauseCode);
        Assert.StartsWith(cause, health.Cause, StringComparison.Ordinal);
        Assert.Contains("For 6m.", health.Cause, StringComparison.Ordinal);
        Assert.Equal("the fix for " + code, health.Remediation);
        Assert.Equal(code, health.Dependency.Code);
    }

    [Theory]
    [InlineData(ServiceDependencyStates.Ok)]
    [InlineData(ServiceDependencyStates.Skipped)]
    [InlineData(ServiceDependencyStates.Unused)]
    public void APassingSkippedOrUnusedProbe_StaysReady(string state)
    {
        ServiceRoleHealth health = Classify(
            "download",
            Beat(Dependency(state, null, "Heroes Profile accepted the API key."))
        );

        Assert.Equal(ServiceRoleState.Ready, health.State);
        Assert.Null(health.CauseCode);
        Assert.Equal(state, health.Dependency.State);
    }

    [Fact]
    public void AStalledSpectateLaunch_StillWins_SoItsRestartIsUnchanged()
    {
        ServiceReadyReport beat = Beat(
            Dependency(ServiceDependencyStates.Unreachable, "spectate.obs_unreachable", "OBS down")
        );
        beat.LastSuccessfulWorkAt = Now.AddMinutes(-40);
        beat.LaunchingSince = Now.AddMinutes(-25);

        ServiceRoleHealth health = Classify("spectate", beat);

        Assert.Equal(ServiceHealthCodes.SpectateLaunchStalled, health.CauseCode);
    }

    [Fact]
    public void AnExitedRoleWithAFailedProbe_IsStillFailed_ByTheProcess_NotTheProbe()
    {
        ServiceReadyReport beat = Beat(
            Dependency(
                ServiceDependencyStates.Rejected,
                "download.heroesprofile_rejected",
                "rejected"
            )
        );

        ServiceRoleHealth health = ServiceHealthClassifier.Classify(
            "download",
            Record("download"),
            running: false,
            beat,
            stopRequested: false,
            Now,
            new ServiceHealthSettings()
        );

        Assert.Equal(ServiceRoleState.Failed, health.State);
        Assert.Null(health.CauseCode);
    }

    [Theory]
    [InlineData("download.heroesprofile_rejected")]
    [InlineData("download.heroesprofile_unreachable")]
    [InlineData("youtube.oauth_invalid")]
    [InlineData("spectate.obs_unreachable")]
    [InlineData("twitch.token_invalid")]
    public void TheRestartPolicy_LeavesADependencyDegradedRoleAlone(string code)
    {
        var ledger = new ServiceRoleRestarts { Role = "download" };
        var health = new ServiceRoleHealth
        {
            Role = code.Split('.')[0],
            State = ServiceRoleState.Degraded,
            Code = ServiceHealthCodes.Degraded,
            CauseCode = code,
        };

        for (int minute = 0; minute < 120; minute++)
        {
            Assert.Equal(
                ServiceRestartAction.None,
                ServiceRestartPolicy.Decide(
                    health,
                    ledger,
                    Now.AddMinutes(minute),
                    new ServiceRestartSettings()
                )
            );
        }

        Assert.Equal(0, ledger.Count);
        Assert.False(ledger.Exhausted);
    }

    [Fact]
    public void StatusJson_ShowsTheCauseCodeTheFixAndTheProbe()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"heroesreplay-services-{Guid.NewGuid():N}.json"
        );
        try
        {
            ServiceLockStore.Save(
                path,
                new ServiceLock
                {
                    StartedAt = Now.AddHours(-1),
                    Processes = new List<ServiceProcessRecord> { Record("download") },
                }
            );
            ServiceReadyReport beat = Beat(
                Dependency(
                    ServiceDependencyStates.Rejected,
                    "download.heroesprofile_rejected",
                    "Heroes Profile rejected HeroesProfileApi:ApiKey (HTTP 401)."
                )
            );
            var output = new StringWriter();
            int code = ServiceSupervisor.Status(
                path,
                pid => pid == 70 ? "heroesreplay" : null,
                spectator: null,
                probeOrNull: _ => null,
                query: new ServiceStatusQuery
                {
                    Output = CliOutputFormat.Json,
                    Out = output,
                    Time = new FixedClock(Now),
                    ReadHeartbeat = _ => beat,
                }
            );

            Assert.Equal(1, code);
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                ServiceHealthCodes.Degraded,
                json.RootElement.GetProperty("code").GetString()
            );
            JsonElement download = json
                .RootElement.GetProperty("roles")
                .EnumerateArray()
                .Single(role => role.GetProperty("role").GetString() == "download");
            Assert.Equal("degraded", download.GetProperty("state").GetString());
            Assert.Equal(
                "download.heroesprofile_rejected",
                download.GetProperty("causeCode").GetString()
            );
            Assert.Equal(
                "the fix for download.heroesprofile_rejected",
                download.GetProperty("remediation").GetString()
            );
            JsonElement dependency = download.GetProperty("dependency");
            Assert.Equal("Heroes Profile API", dependency.GetProperty("name").GetString());
            Assert.Equal("rejected", dependency.GetProperty("state").GetString());

            var text = new StringWriter();
            ServiceSupervisor.Status(
                path,
                pid => pid == 70 ? "heroesreplay" : null,
                spectator: null,
                probeOrNull: _ => null,
                query: new ServiceStatusQuery
                {
                    Out = text,
                    Time = new FixedClock(Now),
                    ReadHeartbeat = _ => beat,
                }
            );
            Assert.Contains("[download.heroesprofile_rejected]", text.ToString());
            Assert.Contains("Fix: the fix for download.heroesprofile_rejected", text.ToString());
            Assert.Contains("Probe: Heroes Profile API rejected, checked 0s ago.", text.ToString());
        }
        finally
        {
            ServiceLockStore.Delete(path);
        }
    }

    private static ServiceRoleHealth Classify(string role, ServiceReadyReport beat) =>
        ServiceHealthClassifier.Classify(
            role,
            Record(role),
            running: true,
            beat,
            stopRequested: false,
            Now,
            new ServiceHealthSettings()
        );

    private static ServiceProcessRecord Record(string role) =>
        new()
        {
            Name = role,
            Pid = 70,
            Nonce = "nonce" + role,
            Arguments = ServiceProcessPlan.ArgumentsFor(role),
            Version = "1.2.3",
        };

    private static ServiceRoleDependency Dependency(
        string state,
        string code,
        string cause,
        DateTimeOffset? since = null
    ) =>
        new()
        {
            Name = "Heroes Profile API",
            State = state,
            Code = code,
            Cause = cause,
            Remediation = code == null ? null : "the fix for " + code,
            CheckedAt = Now,
            Since = since ?? Now,
        };

    private static ServiceReadyReport Beat(ServiceRoleDependency dependency) =>
        new()
        {
            Version = "1.2.3",
            Readiness = ServiceReadiness.Ready,
            ReadyAt = Now.AddHours(-1),
            HeartbeatAt = Now.AddSeconds(-3),
            HeartbeatIntervalSeconds = 15,
            LastSuccessfulWorkAt = Now.AddMinutes(-1),
            Dependency = dependency,
        };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
