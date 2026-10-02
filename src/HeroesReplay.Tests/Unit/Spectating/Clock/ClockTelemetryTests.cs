using System;
using HeroesReplay.Core.Spectating.Clock.Memory;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Clock;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ClockTelemetryTests
{
    [Fact]
    public void Describe_ReportsDiscoveringUntilTheScanFinishes()
    {
        ClockTelemetryReport report = ClockTelemetry.Describe(false, false, "pattern");

        Assert.Equal(ClockTelemetry.Discovering, report.State);
        Assert.Equal("pattern", report.Reason);
    }

    [Fact]
    public void Describe_ReportsMemoryLockedOnlyForATrustedRead()
    {
        ClockTelemetryReport report = ClockTelemetry.Describe(true, true, "ok");

        Assert.Equal(ClockTelemetry.MemoryLocked, report.State);
        Assert.Equal("ok", report.Reason);
    }

    [Fact]
    public void Describe_ReportsUnlockedWithTheReadReason()
    {
        ClockTelemetryReport missing = ClockTelemetry.Describe(true, false, "read-failed");
        ClockTelemetryReport stalled = ClockTelemetry.Describe(true, true, "stalled");
        ClockTelemetryReport malformed = ClockTelemetry.Describe(true, false, "bad-scale");

        Assert.Equal(ClockTelemetry.Unlocked, missing.State);
        Assert.Equal("read-failed", missing.Reason);
        Assert.Equal(ClockTelemetry.Unlocked, stalled.State);
        Assert.Equal("stalled", stalled.Reason);
        Assert.Equal(ClockTelemetry.Unlocked, malformed.State);
        Assert.Equal("bad-scale", malformed.Reason);
    }

    [Fact]
    public void Changed_IsFalseWhenTheStateAndReasonStayTheSame()
    {
        var report = new ClockTelemetryReport(ClockTelemetry.Unlocked, "read-failed");

        Assert.False(ClockTelemetry.Changed(report, report));
        Assert.True(
            ClockTelemetry.Changed(
                report,
                new ClockTelemetryReport(ClockTelemetry.MemoryLocked, "ok")
            )
        );
    }

    [Fact]
    public void Read_TransientFailureReportsOnceAndDoesNotScanAgain()
    {
        using var clock = new StableMatchClock();
        var module = new StableClockModule(
            9,
            0x140000000,
            0x3400000,
            MatchTickClock.SupportedBuild
        );

        StableClockSample sample = clock.Read(module, (_, _) => false);

        Assert.Equal("read-failed", sample.Reason);
        Assert.Equal(ClockTelemetry.Describe(false, false, "no-process"), clock.DiscoveryTelemetry);
        Assert.Equal(ClockTelemetry.Describe(true, false, "read-failed"), clock.LastTelemetry);
        int emitted = clock.TelemetryEmissions;
        Assert.True(emitted >= 2);

        clock.Read(module, (_, _) => false);

        Assert.Equal(emitted, clock.TelemetryEmissions);
        Assert.Equal("read-failed", clock.LastTelemetry.Reason);
    }

    [Fact]
    public void Read_ModuleChangeReportsDiscoveryAgain()
    {
        using var clock = new StableMatchClock();
        Func<long, byte[], bool> read = (_, _) => false;
        clock.Read(new StableClockModule(4, 0x140000000, 0x20000, "0.0.0.0"), read);
        int emitted = clock.TelemetryEmissions;

        clock.Read(new StableClockModule(5, 0x150000000, 0x20000, "0.0.0.1"), read);

        Assert.True(clock.TelemetryEmissions > emitted);
        Assert.Equal(ClockTelemetry.Discovering, clock.DiscoveryTelemetry.State);
        Assert.Equal(ClockTelemetry.Unlocked, clock.LastTelemetry.State);
    }
}
