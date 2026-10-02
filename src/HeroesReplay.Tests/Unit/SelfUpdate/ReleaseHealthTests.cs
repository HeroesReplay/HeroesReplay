using System;
using System.Collections.Generic;
using HeroesReplay.Core.SelfUpdate;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.SelfUpdate;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReleaseHealthTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(20);

    [Fact]
    public void EveryRoleFreshAndSpectateProgressAfterTheInstall_IsHealthy()
    {
        ReleaseHealthResult result = Judge(Stack(), at: Since.AddMinutes(6));

        Assert.Equal(ReleaseHealthVerdict.Healthy, result.Verdict);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Problems);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("stale")]
    [InlineData("old-process")]
    [InlineData("not-running")]
    [InlineData("stopping")]
    public void OneRoleWithoutAFreshHeartbeatFromTheNewInstall_IsNotHealthy(string fault)
    {
        List<ServiceRoleHealth> roles = Stack();
        ServiceRoleHealth download = roles[2];
        roles[2] = fault switch
        {
            "missing" => download with { HeartbeatAgeSeconds = null },
            "stale" => download with { HeartbeatAgeSeconds = 46 },
            "old-process" => download with { ReadyAt = Since.AddSeconds(-1) },
            "not-running" => download with { Running = false, State = ServiceRoleState.Failed },
            _ => download with { Readiness = ServiceReadiness.Stopping },
        };

        ReleaseHealthResult inside = Judge(roles, at: Since.AddMinutes(19));
        ReleaseHealthResult after = Judge(roles, at: Since.Add(Window));

        Assert.Equal(ReleaseHealthVerdict.Waiting, inside.Verdict);
        Assert.Equal(1, inside.ExitCode);
        Assert.Equal(ReleaseHealthVerdict.Unhealthy, after.Verdict);
        Assert.Equal(2, after.ExitCode);
        Assert.Single(after.Problems);
        Assert.StartsWith("download", after.Problems[0]);
    }

    [Fact]
    public void SpectateWithoutMatchProgressSinceTheInstall_RollsBackWhenTheWindowCloses()
    {
        List<ServiceRoleHealth> roles = Stack();
        roles[0] = roles[0] with
        {
            // Match progress from the build before the install does not count.
            LastSuccessfulWorkAt = Since.AddMinutes(-3),
            SessionsWithoutProgress = 3,
            LastOutcome = "LoadTimedOut",
        };

        ReleaseHealthResult waiting = Judge(roles, at: Since.AddMinutes(10));
        ReleaseHealthResult result = Judge(roles, at: Since.AddMinutes(21));

        Assert.Equal(ReleaseHealthVerdict.Waiting, waiting.Verdict);
        Assert.Equal(ReleaseHealthVerdict.Unhealthy, result.Verdict);
        string problem = Assert.Single(result.Problems);
        Assert.Contains("spectate has shown no match progress since the install", problem);
        Assert.Contains("3 sessions without it, the last LoadTimedOut", problem);
        Assert.Contains("unhealthy: spectate", result.Describe());
    }

    [Fact]
    public void TheWindowIsConfigurable_AndZeroMeansTheDefault()
    {
        List<ServiceRoleHealth> roles = Stack();
        roles[0] = roles[0] with { LastSuccessfulWorkAt = null };

        Assert.Equal(
            ReleaseHealthVerdict.Unhealthy,
            ReleaseHealth
                .Judge(roles, false, Since, Since.AddMinutes(5), TimeSpan.FromMinutes(5))
                .Verdict
        );
        Assert.Equal(
            ReleaseHealthVerdict.Waiting,
            ReleaseHealth.Judge(roles, false, Since, Since.AddMinutes(19), TimeSpan.Zero).Verdict
        );
        Assert.Equal(TimeSpan.FromMinutes(20), ReleaseHealth.Window(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMinutes(5), ReleaseHealth.Window(TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void NoStack_OrNoSpectate_IsNotHealthy()
    {
        List<ServiceRoleHealth> withoutSpectate = Stack();
        withoutSpectate[0] = withoutSpectate[0] with { Expected = false };

        ReleaseHealthResult empty = Judge(new List<ServiceRoleHealth>(), at: Since.Add(Window));
        ReleaseHealthResult noSpectate = Judge(withoutSpectate, at: Since.Add(Window));

        Assert.Equal(ReleaseHealthVerdict.Unhealthy, empty.Verdict);
        Assert.Contains("services.json lists no roles", empty.Problems[0]);
        Assert.Equal(ReleaseHealthVerdict.Unhealthy, noSpectate.Verdict);
        Assert.Contains("spectate is not in services.json", noSpectate.Problems[0]);
    }

    [Fact]
    public void AStopRequest_IsNotJudged()
    {
        ReleaseHealthResult result = ReleaseHealth.Judge(
            new List<ServiceRoleHealth>(),
            stopRequested: true,
            Since,
            Since.AddHours(1),
            Window
        );

        Assert.Equal(ReleaseHealthVerdict.Stopped, result.Verdict);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public void ADegradedRoleWithAFreshHeartbeat_StillCountsAsUp()
    {
        List<ServiceRoleHealth> roles = Stack();
        roles[1] = roles[1] with { State = ServiceRoleState.Degraded };

        Assert.Equal(ReleaseHealthVerdict.Healthy, Judge(roles, at: Since.AddMinutes(6)).Verdict);
    }

    private static ReleaseHealthResult Judge(
        IEnumerable<ServiceRoleHealth> roles,
        DateTimeOffset at
    ) => ReleaseHealth.Judge(roles, stopRequested: false, Since, at, Window);

    private static List<ServiceRoleHealth> Stack() =>
        new()
        {
            Role("spectate") with
            {
                LastSuccessfulWorkAt = Since.AddMinutes(5),
                SessionsWithoutProgress = 0,
            },
            Role("twitch"),
            Role("download"),
            Role("youtube"),
        };

    private static ServiceRoleHealth Role(string name) =>
        new()
        {
            Role = name,
            State = ServiceRoleState.Ready,
            Expected = true,
            Running = true,
            Readiness = ServiceReadiness.Ready,
            ReadyAt = Since.AddSeconds(40),
            HeartbeatAgeSeconds = 5,
            StaleAfterSeconds = 45,
        };
}
