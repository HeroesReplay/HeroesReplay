using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.ServiceHost;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ServiceRestartPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly ServiceRestartSettings Defaults = new();

    [Fact]
    public void Backoff_Is10s30s2m5m_AndTheLastDelayRepeats()
    {
        Assert.Equal(
            new[] { 10, 30, 120, 300, 300, 300 },
            Enumerable.Range(0, 6).Select(used => (int)Defaults.BackoffFor(used).TotalSeconds)
        );
        Assert.Equal(5, Defaults.Limit);
        Assert.Equal(TimeSpan.FromMinutes(30), Defaults.Window);
        Assert.Equal(TimeSpan.FromMinutes(2), Defaults.StaleLimit);
    }

    [Fact]
    public void Settings_BindReplacesTheBackoffAndKeepsDefaultsForTheRest()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["ServiceRestart:Backoff:0"] = "00:00:05",
                    ["ServiceRestart:Backoff:1"] = "00:01:00",
                    ["ServiceRestart:Budget"] = "3",
                }
            )
            .Build();

        ServiceRestartSettings bound = configuration
            .GetSection("ServiceRestart")
            .Get<ServiceRestartSettings>();

        Assert.Equal(new[] { 5, 60 }, bound.Delays.Select(delay => (int)delay.TotalSeconds));
        Assert.Equal(3, bound.Limit);
        Assert.Equal(TimeSpan.FromMinutes(30), bound.Window);
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            new ServiceRestartSettings { Backoff = new List<TimeSpan>() }.BackoffFor(0)
        );
    }

    [Fact]
    public void ReadyWait_Is45s_And3mWhileTheCommitChargeIsAbove90Percent()
    {
        // #397: a role that thrashes on start is waited for, not taken for a failed start.
        Assert.Equal(TimeSpan.FromSeconds(45), Defaults.ReadyWaitFor(null));
        Assert.Equal(TimeSpan.FromSeconds(45), Defaults.ReadyWaitFor(90));
        Assert.Equal(TimeSpan.FromMinutes(3), Defaults.ReadyWaitFor(90.1));
        Assert.Equal(TimeSpan.FromMinutes(3), Defaults.ReadyWaitFor(98));

        var odd = new ServiceRestartSettings
        {
            ReadyTimeout = TimeSpan.FromMinutes(2),
            SlowReadyTimeout = TimeSpan.FromSeconds(30),
            SlowReadyCommitPercent = 80,
        };
        Assert.Equal(TimeSpan.FromMinutes(2), odd.ReadyWaitFor(85));
        Assert.Equal(
            TimeSpan.FromSeconds(45),
            new ServiceRestartSettings { ReadyTimeout = TimeSpan.Zero }.ReadyWait
        );

        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["ServiceRestart:SlowReadyTimeout"] = "00:05:00",
                    ["ServiceRestart:SlowReadyCommitPercent"] = "95",
                }
            )
            .Build();
        ServiceRestartSettings bound = configuration
            .GetSection("ServiceRestart")
            .Get<ServiceRestartSettings>();
        Assert.Equal(TimeSpan.FromSeconds(45), bound.ReadyWaitFor(94));
        Assert.Equal(TimeSpan.FromMinutes(5), bound.ReadyWaitFor(96));
    }

    [Fact]
    public void Adopted_ClearsTheDownStateAndTheFailure_AndUsesNoBudget()
    {
        var ledger = new ServiceRoleRestarts { Role = "spectate", Nonce = "old" };
        Assert.Equal(ServiceRestartAction.Scheduled, Decide(Failed(), ledger, Now));
        ServiceRestartPolicy.Restarted(
            ledger,
            Now.AddSeconds(10),
            null,
            "Failed to start spectate."
        );
        Assert.Equal(ServiceRestartAction.Scheduled, Decide(Failed(), ledger, Now.AddSeconds(11)));

        ServiceRestartPolicy.Adopted(ledger, Now.AddSeconds(41), "n21960", 21960);

        Assert.Equal(1, ledger.Adopted);
        Assert.Equal(Now.AddSeconds(41), ledger.LastAdoptedAt);
        Assert.Equal(21960, ledger.LastAdoptedPid);
        Assert.Equal("n21960", ledger.Nonce);
        Assert.Null(ledger.LastFailure);
        Assert.Null(ledger.DownSince);
        Assert.Null(ledger.NextRestartAt);
        Assert.Equal(1, ledger.Count);
        Assert.Single(ledger.Recent);
        Assert.Equal(ServiceRestartAction.None, Decide(Ready(), ledger, Now.AddSeconds(42)));
    }

    [Fact]
    public void Failed_SchedulesTheBackoff_ThenRestartsWhenItIsDue()
    {
        var ledger = new ServiceRoleRestarts { Role = "download" };

        Assert.Equal(ServiceRestartAction.Scheduled, Decide(Failed(), ledger, Now));
        Assert.Equal(Now.AddSeconds(10), ledger.NextRestartAt);
        Assert.Equal(ServiceRestartPolicy.FailedReason, ledger.DownReason);
        Assert.Equal(ServiceRestartAction.None, Decide(Failed(), ledger, Now.AddSeconds(9)));
        Assert.Equal(ServiceRestartAction.Restart, Decide(Failed(), ledger, Now.AddSeconds(10)));
    }

    [Fact]
    public void EachRestartInTheWindow_MovesToTheNextBackoff()
    {
        var ledger = new ServiceRoleRestarts { Role = "download" };
        DateTimeOffset at = Now;
        var waits = new List<double>();
        for (int restart = 0; restart < 5; restart++)
        {
            Assert.Equal(ServiceRestartAction.Scheduled, Decide(Failed(), ledger, at));
            waits.Add((ledger.NextRestartAt.Value - at).TotalSeconds);
            at = ledger.NextRestartAt.Value;
            Assert.Equal(ServiceRestartAction.Restart, Decide(Failed(), ledger, at));
            ServiceRestartPolicy.Restarted(ledger, at, "n" + restart, null);
            at = at.AddSeconds(5);
            Assert.Equal(ServiceRestartAction.None, Decide(Ready(), ledger, at));
        }

        Assert.Equal(new double[] { 10, 30, 120, 300, 300 }, waits);
        Assert.Equal(5, ledger.Count);
        Assert.Equal("n4", ledger.Nonce);
    }

    [Fact]
    public void ASixthFailureInsideTheWindow_ExhaustsTheBudgetForGood()
    {
        var ledger = new ServiceRoleRestarts { Role = "youtube" };
        for (int restart = 0; restart < 5; restart++)
        {
            ServiceRestartPolicy.Restarted(ledger, Now.AddMinutes(restart), null, null);
        }

        Assert.Equal(ServiceRestartAction.Exhausted, Decide(Failed(), ledger, Now.AddMinutes(10)));
        Assert.True(ledger.Exhausted);
        Assert.Equal(Now.AddMinutes(10), ledger.ExhaustedAt);
        Assert.Null(ledger.NextRestartAt);

        // Exhausted stays exhausted, even after the window has passed.
        Assert.Equal(ServiceRestartAction.None, Decide(Failed(), ledger, Now.AddHours(5)));
        Assert.True(ledger.Exhausted);
    }

    [Fact]
    public void RestartsOlderThanTheWindow_StopCountingAgainstTheBudget()
    {
        var ledger = new ServiceRoleRestarts { Role = "download" };
        for (int restart = 0; restart < 5; restart++)
        {
            ServiceRestartPolicy.Restarted(ledger, Now.AddMinutes(restart), null, null);
        }

        DateTimeOffset later = Now.AddMinutes(30).AddSeconds(30);
        Assert.Equal(ServiceRestartAction.Scheduled, Decide(Failed(), ledger, later));
        Assert.Equal(4, ledger.Recent.Count);
        Assert.Equal(later.AddMinutes(5), ledger.NextRestartAt);
    }

    [Fact]
    public void AFailedAttempt_CountsAndTheNextFailureWaitsTheNextBackoff()
    {
        var ledger = new ServiceRoleRestarts { Role = "download" };
        Decide(Failed(), ledger, Now);
        ServiceRestartPolicy.Restarted(
            ledger,
            Now.AddSeconds(10),
            "n1",
            "download failed: ready timed out."
        );

        Assert.Equal(1, ledger.Count);
        Assert.Equal("download failed: ready timed out.", ledger.LastFailure);
        Assert.Equal(ServiceRestartAction.Scheduled, Decide(Failed(), ledger, Now.AddSeconds(56)));
        Assert.Equal(Now.AddSeconds(86), ledger.NextRestartAt);
    }

    [Fact]
    public void Stale_IsKilledOnlyOnceTheHeartbeatIsOlderThanTheLimit()
    {
        var ledger = new ServiceRoleRestarts { Role = "download" };

        Assert.Equal(ServiceRestartAction.None, Decide(Stale(heartbeatAge: 60), ledger, Now));
        Assert.Equal(ServiceRestartAction.None, Decide(Stale(heartbeatAge: 119), ledger, Now));
        Assert.Equal(ServiceRestartAction.Kill, Decide(Stale(heartbeatAge: 120), ledger, Now));
        Assert.Equal(ServiceRestartPolicy.StaleReason, ledger.DownReason);

        // Once killed it is failed, waits the backoff, and the restart says stale.
        Assert.Equal(ServiceRestartAction.Scheduled, Decide(Failed(), ledger, Now.AddSeconds(1)));
        Assert.Equal(ServiceRestartAction.Restart, Decide(Failed(), ledger, Now.AddSeconds(11)));
        ServiceRestartPolicy.Restarted(ledger, Now.AddSeconds(11), "n2", null);
        Assert.Equal(ServiceRestartPolicy.StaleReason, ledger.LastReason);
        Assert.Null(ledger.DownReason);
    }

    [Fact]
    public void StaleWithoutAHeartbeatFile_CountsFromWhenTheSupervisorFirstSawIt()
    {
        var ledger = new ServiceRoleRestarts { Role = "download" };

        Assert.Equal(ServiceRestartAction.None, Decide(Stale(heartbeatAge: null), ledger, Now));
        Assert.Equal(
            ServiceRestartAction.None,
            Decide(Stale(heartbeatAge: null), ledger, Now.AddSeconds(119))
        );
        Assert.Equal(
            ServiceRestartAction.Kill,
            Decide(Stale(heartbeatAge: null), ledger, Now.AddSeconds(120))
        );
    }

    [Theory]
    [InlineData(ServiceRoleState.Ready)]
    [InlineData(ServiceRoleState.Degraded)]
    [InlineData(ServiceRoleState.Stopped)]
    public void HealthyDegradedOrStopped_IsLeftAloneAndClearsADownRole(ServiceRoleState state)
    {
        var ledger = new ServiceRoleRestarts { Role = "twitch" };
        Decide(Failed(), ledger, Now);

        Assert.Equal(
            ServiceRestartAction.None,
            Decide(
                new ServiceRoleHealth { Role = "twitch", State = state },
                ledger,
                Now.AddSeconds(30)
            )
        );
        Assert.Null(ledger.DownSince);
        Assert.Null(ledger.NextRestartAt);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void StalledSpectateLaunch_IsKilledThenRestartsLikeAFailedRole()
    {
        var ledger = new ServiceRoleRestarts { Role = "spectate" };

        Assert.Equal(ServiceRestartAction.Kill, Decide(Stalled(), ledger, Now));
        Assert.Equal(ServiceRestartPolicy.StalledReason, ledger.DownReason);

        ServiceRoleHealth killed = Failed() with { Role = "spectate" };
        Assert.Equal(ServiceRestartAction.Scheduled, Decide(killed, ledger, Now.AddSeconds(1)));
        Assert.Equal(ServiceRestartAction.Restart, Decide(killed, ledger, Now.AddSeconds(11)));
        ServiceRestartPolicy.Restarted(ledger, Now.AddSeconds(11), "n2", failure: null);
        Assert.Equal(ServiceRestartPolicy.StalledReason, ledger.LastReason);
        Assert.Single(ledger.Recent);
    }

    [Fact]
    public void StalledSpectateLaunch_WithNoBudgetLeft_IsLeftUpAndDegraded()
    {
        var ledger = new ServiceRoleRestarts { Role = "spectate" };
        for (int i = 0; i < Defaults.Limit; i++)
        {
            ledger.Recent.Add(Now.AddMinutes(-i));
        }

        Assert.Equal(ServiceRestartAction.None, Decide(Stalled(), ledger, Now));
        Assert.False(ledger.Exhausted);
    }

    [Fact]
    public void OtherDegradedCauses_AreStillLeftAlone()
    {
        var ledger = new ServiceRoleRestarts { Role = "spectate" };

        Assert.Equal(
            ServiceRestartAction.None,
            Decide(
                Stalled() with
                {
                    CauseCode = ServiceHealthCodes.SpectateNoMatchProgress,
                },
                ledger,
                Now
            )
        );
        Assert.Equal(
            ServiceRestartAction.None,
            Decide(Stalled() with { CauseCode = null }, ledger, Now)
        );
    }

    private static ServiceRoleHealth Stalled() =>
        new()
        {
            Role = "spectate",
            State = ServiceRoleState.Degraded,
            CauseCode = ServiceHealthCodes.SpectateLaunchStalled,
            Cause = "One replay has been launching or loading for 21m with no match progress.",
        };

    private static ServiceRestartAction Decide(
        ServiceRoleHealth health,
        ServiceRoleRestarts ledger,
        DateTimeOffset at
    ) => ServiceRestartPolicy.Decide(health, ledger, at, Defaults);

    private static ServiceRoleHealth Failed() =>
        new()
        {
            Role = "download",
            State = ServiceRoleState.Failed,
            Cause = "pid 7 exited unexpectedly.",
        };

    private static ServiceRoleHealth Ready() =>
        new() { Role = "download", State = ServiceRoleState.Ready };

    private static ServiceRoleHealth Stale(long? heartbeatAge) =>
        new()
        {
            Role = "download",
            State = ServiceRoleState.Stale,
            HeartbeatAgeSeconds = heartbeatAge,
            Cause = "pid 7 is running but its heartbeat is old.",
        };
}
