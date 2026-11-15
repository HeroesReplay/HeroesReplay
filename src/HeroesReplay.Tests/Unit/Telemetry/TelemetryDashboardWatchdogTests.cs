using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Telemetry;
using Xunit;

namespace HeroesReplay.Tests.Unit.Telemetry;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class TelemetryDashboardWatchdogTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 20, 20, 0, TimeSpan.Zero);

    private sealed class Fixture
    {
        public bool Listening;
        public int Relaunches;
        public Func<string> OnRelaunch;
        public TelemetryDashboardWatchdog Watchdog;

        public Fixture(TelemetryDashboardSettings settings = null, bool enabled = true)
        {
            OnRelaunch = () =>
            {
                Listening = true;
                return null;
            };
            Watchdog = new TelemetryDashboardWatchdog(
                settings ?? new TelemetryDashboardSettings(),
                enabled
            )
            {
                Listening = () => Listening,
                Relaunch = () =>
                {
                    Relaunches++;
                    return OnRelaunch();
                },
                // Inline, so a pass that starts a relaunch has its result on the next pass.
                Run = work => Task.FromResult(work()),
            };
        }
    }

    [Fact]
    public void Tick_WhenOtlpAnswers_IsUpAndRelaunchesNothing()
    {
        var fixture = new Fixture { Listening = true };

        Assert.True(fixture.Watchdog.Tick(T0));

        Assert.Equal(TelemetryDashboardStates.Up, fixture.Watchdog.State.State);
        Assert.Equal(0, fixture.Relaunches);
        Assert.False(fixture.Watchdog.State.Degraded);
    }

    [Fact]
    public void Tick_WhenTheDashboardDies_RelaunchesItAndReportsItBack()
    {
        var fixture = new Fixture { Listening = true };
        fixture.Watchdog.Tick(T0);

        fixture.Listening = false;
        DateTimeOffset died = T0 + TimeSpan.FromMinutes(1);
        Assert.True(fixture.Watchdog.Tick(died));

        Assert.Equal(1, fixture.Relaunches);
        Assert.Equal(TelemetryDashboardStates.Restarting, fixture.Watchdog.State.State);
        Assert.True(fixture.Watchdog.State.Degraded);
        Assert.Equal(died, fixture.Watchdog.State.DownSince);

        Assert.True(fixture.Watchdog.Tick(died + TimeSpan.FromSeconds(2)));
        Assert.Equal(TelemetryDashboardStates.Up, fixture.Watchdog.State.State);
        Assert.Equal(1, fixture.Watchdog.State.Restarts);
        Assert.Null(fixture.Watchdog.State.DownSince);
    }

    [Fact]
    public void Tick_WhileTheRelaunchKeepsFailing_BacksOffAndKeepsTrying()
    {
        var settings = new TelemetryDashboardSettings
        {
            CheckInterval = TimeSpan.FromMinutes(1),
            RestartDelays = new() { TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15) },
        };
        var fixture = new Fixture(settings) { OnRelaunch = () => "the Aspire CLI was not found" };

        fixture.Watchdog.Tick(T0);
        fixture.Watchdog.Tick(T0 + TimeSpan.FromSeconds(1));
        Assert.Equal(1, fixture.Relaunches);
        Assert.Equal(TelemetryDashboardStates.Down, fixture.Watchdog.State.State);
        Assert.Equal("the Aspire CLI was not found", fixture.Watchdog.State.LastFailure);
        Assert.Equal(
            T0 + TimeSpan.FromSeconds(1) + TimeSpan.FromMinutes(5),
            fixture.Watchdog.State.NextRestartAt
        );

        // Probed every minute, but not relaunched before its backoff.
        for (int minute = 1; minute < 5; minute++)
        {
            fixture.Watchdog.Tick(T0 + TimeSpan.FromMinutes(minute) + TimeSpan.FromSeconds(2));
        }

        Assert.Equal(1, fixture.Relaunches);

        fixture.Watchdog.Tick(T0 + TimeSpan.FromMinutes(6));
        Assert.Equal(2, fixture.Relaunches);
        fixture.Watchdog.Tick(T0 + TimeSpan.FromMinutes(6) + TimeSpan.FromSeconds(1));
        Assert.Equal(
            T0 + TimeSpan.FromMinutes(6) + TimeSpan.FromSeconds(1) + TimeSpan.FromMinutes(15),
            fixture.Watchdog.State.NextRestartAt
        );
    }

    [Fact]
    public void Tick_WhenARelaunchThrows_RecordsTheFailureAndDoesNotThrow()
    {
        var fixture = new Fixture { OnRelaunch = () => throw new InvalidOperationException("boom") };

        fixture.Watchdog.Tick(T0);
        fixture.Watchdog.Tick(T0 + TimeSpan.FromSeconds(1));

        Assert.Equal(TelemetryDashboardStates.Down, fixture.Watchdog.State.State);
        Assert.Equal("boom", fixture.Watchdog.State.LastFailure);
    }

    [Fact]
    public void Tick_WithinTheCheckInterval_DoesNotProbe()
    {
        int probes = 0;
        var watchdog = new TelemetryDashboardWatchdog(new TelemetryDashboardSettings(), true)
        {
            Listening = () =>
            {
                probes++;
                return true;
            },
        };

        watchdog.Tick(T0);
        watchdog.Tick(T0 + TimeSpan.FromSeconds(30));
        watchdog.Tick(T0 + TimeSpan.FromSeconds(59));
        Assert.Equal(1, probes);

        watchdog.Tick(T0 + TimeSpan.FromSeconds(60));
        Assert.Equal(2, probes);
    }

    [Theory]
    [InlineData(false, true, "OpenTelemetry:Enabled is false.")]
    [InlineData(true, false, "OpenTelemetry:Dashboard:Supervise is false.")]
    public void Off_NeverProbesOrRelaunches(bool telemetry, bool supervise, string reason)
    {
        var fixture = new Fixture(
            new TelemetryDashboardSettings { Supervise = supervise },
            telemetry
        );

        Assert.False(fixture.Watchdog.Tick(T0));

        Assert.Equal(0, fixture.Relaunches);
        Assert.Equal(TelemetryDashboardStates.Off, fixture.Watchdog.State.State);
        Assert.Equal(reason, fixture.Watchdog.State.Reason);
    }

    [Fact]
    public void Describe_WhenDown_NamesTheCodeAndTheNextRelaunch()
    {
        var state = new TelemetryDashboardState
        {
            State = TelemetryDashboardStates.Down,
            DownSince = T0,
            NextRestartAt = T0 + TimeSpan.FromHours(6) + TimeSpan.FromMinutes(5),
            Restarts = 3,
            LastFailure = "the Aspire CLI was not found",
        };

        string text = state.Describe(T0 + TimeSpan.FromHours(6));

        Assert.Contains("down [telemetry.dashboard_down] for 6h", text);
        Assert.Contains("next relaunch in 5m", text);
        Assert.Contains("relaunches 3", text);
        Assert.Contains("the Aspire CLI was not found", text);
    }
}
