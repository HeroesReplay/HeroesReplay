using System;
using HeroesReplay.Core.Obs;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

/// <summary>
/// The stream's health is more than <c>outputActive</c> (#395). On 2026-10-09 production reported
/// <c>outputActive: true, outputReconnecting: true</c> with <c>outputBytes</c> frozen at
/// 199,373,948,174 for 4 h 11 min, and that counted as live.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ObsStreamHealthTests
{
    private const long ProductionBytes = 199_373_948_174;
    private static readonly DateTimeOffset T0 = new(2026, 10, 9, 5, 51, 48, TimeSpan.Zero);

    [Fact]
    public void ActiveButReconnecting_IsNotLive()
    {
        ObsStreamHealth health = ObsStreamHealth.Next(
            null,
            new ObsStreamSample(true, true, ProductionBytes),
            T0
        );

        Assert.Equal(ObsStreamState.Reconnecting, health.State);
        Assert.False(health.IsLive);
        Assert.True(health.OutputActive);
        Assert.Equal(T0, health.StuckSince);
        Assert.Equal(ObsStreamHealth.StuckReconnectingCode, health.CauseCode);
        Assert.Contains("199373948174 bytes", health.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ReconnectingForHours_KeepsWhenItBegan()
    {
        ObsStreamHealth health = null;
        for (int minute = 0; minute <= 251; minute++)
        {
            health = ObsStreamHealth.Next(
                health,
                new ObsStreamSample(true, true, ProductionBytes),
                T0.AddMinutes(minute)
            );
        }

        Assert.Equal(ObsStreamState.Reconnecting, health.State);
        Assert.Equal(T0, health.StuckSince);
        Assert.Equal(TimeSpan.FromMinutes(251), health.StuckFor(T0.AddMinutes(251)));
    }

    [Fact]
    public void BytesThatMove_AreLive()
    {
        ObsStreamHealth first = ObsStreamHealth.Next(
            null,
            new ObsStreamSample(true, false, 100),
            T0
        );
        ObsStreamHealth second = ObsStreamHealth.Next(
            first,
            new ObsStreamSample(true, false, 22_000_000),
            T0.AddSeconds(15)
        );

        Assert.Equal(ObsStreamState.Live, first.State);
        Assert.Equal(ObsStreamState.Live, second.State);
        Assert.True(second.IsLive);
        Assert.Null(second.StuckSince);
        Assert.Null(second.CauseCode);
    }

    [Fact]
    public void FrozenBytes_AreStalledOnlyAfterTheStallWindow()
    {
        ObsStreamHealth first = ObsStreamHealth.Next(
            null,
            new ObsStreamSample(true, false, ProductionBytes),
            T0
        );
        ObsStreamHealth soon = ObsStreamHealth.Next(
            first,
            new ObsStreamSample(true, false, ProductionBytes),
            T0 + ObsStreamHealth.StallWindow - TimeSpan.FromSeconds(1)
        );
        ObsStreamHealth later = ObsStreamHealth.Next(
            soon,
            new ObsStreamSample(true, false, ProductionBytes),
            T0 + ObsStreamHealth.StallWindow
        );

        // Two reads close together must not look frozen.
        Assert.Equal(ObsStreamState.Live, soon.State);
        Assert.Equal(ObsStreamState.Stalled, later.State);
        Assert.False(later.IsLive);
        Assert.True(later.OutputActive);
        Assert.Equal(T0, later.StuckSince);
        Assert.Equal(ObsStreamHealth.StalledCode, later.CauseCode);
    }

    [Fact]
    public void AStreamThatJustStarted_IsNotFrozenAgainstTheStoppedOne()
    {
        ObsStreamHealth stopped = ObsStreamHealth.Next(
            null,
            new ObsStreamSample(false, false, 0),
            T0
        );
        ObsStreamHealth started = ObsStreamHealth.Next(
            stopped,
            new ObsStreamSample(true, false, 0),
            T0.AddMinutes(5)
        );

        Assert.Equal(ObsStreamState.Inactive, stopped.State);
        Assert.False(stopped.OutputActive);
        Assert.Equal(ObsStreamState.Live, started.State);
    }

    [Fact]
    public void AnUnreadableObs_DoesNotRestartTheStuckClock()
    {
        ObsStreamHealth stuck = ObsStreamHealth.Next(
            null,
            new ObsStreamSample(true, true, ProductionBytes),
            T0
        );
        ObsStreamHealth missed = ObsStreamHealth.Unknown(stuck, "timed out", T0.AddSeconds(30));
        ObsStreamHealth again = ObsStreamHealth.Next(
            missed,
            new ObsStreamSample(true, true, ProductionBytes),
            T0.AddSeconds(60)
        );

        Assert.Equal(ObsStreamState.Unknown, missed.State);
        Assert.False(missed.IsLive);
        Assert.False(missed.OutputActive);
        Assert.Equal("timed out", missed.Detail);
        Assert.Equal(T0, again.StuckSince);
    }

    [Fact]
    public void Live_AfterReconnecting_EndsTheStretch()
    {
        ObsStreamHealth stuck = ObsStreamHealth.Next(
            null,
            new ObsStreamSample(true, true, ProductionBytes),
            T0
        );
        ObsStreamHealth back = ObsStreamHealth.Next(
            stuck,
            new ObsStreamSample(true, false, ProductionBytes + 1_000_000),
            T0.AddSeconds(20)
        );

        Assert.Equal(ObsStreamState.Live, back.State);
        Assert.Null(back.StuckSince);
        Assert.Equal(TimeSpan.Zero, back.StuckFor(T0.AddMinutes(5)));
    }

    [Fact]
    public void TheRawGetStreamStatusAnswer_IsReadTheSameWay()
    {
        // The supervisor and the fail-safe read OBS through a read-only session (JSON).
        FakeObs obs = FakeObs.Packaged();
        obs.StreamActive = true;
        obs.StreamReconnecting = true;
        obs.StreamBytes = ProductionBytes;
        using var session = obs.Open("ws://127.0.0.1:4455", string.Empty);

        ObsStreamHealth health = ObsStreamHealth.Next(null, session.Get("GetStreamStatus"), T0);

        Assert.Equal(ObsStreamState.Reconnecting, health.State);
        Assert.Equal(ProductionBytes, health.Bytes);
        Assert.Equal(new[] { "GetStreamStatus" }, obs.Requests);
    }

    [Fact]
    public void AnAnswerWithoutOutputActive_IsUnknown()
    {
        ObsStreamHealth health = ObsStreamHealth.Next(null, new JObject(), T0);

        Assert.Equal(ObsStreamState.Unknown, health.State);
        Assert.False(health.IsLive);
    }
}
