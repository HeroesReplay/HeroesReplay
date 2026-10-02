using System;
using HeroesReplay.Core.Spectating.Clock;
using HeroesReplay.Core.Spectating.Clock.Hybrid;
using HeroesReplay.Core.Spectating.Clock.Memory;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Clock;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class FallbackGameTimerTests
{
    [Fact]
    public void Select_UsesMemoryWhenItIsUsable()
    {
        GameTimerReading chosen = FallbackGameTimer.Select(
            new GameTimerReading(
                true,
                "memory",
                "ok",
                TimeSpan.FromSeconds(12),
                4096 * 12,
                1f / 4096f
            ),
            new GameTimerReading(true, "ocr", "ok", TimeSpan.FromSeconds(99))
        );

        Assert.Equal("memory", chosen.Source);
        Assert.Equal(TimeSpan.FromSeconds(12), chosen.Time);
        Assert.Equal(ClockTelemetry.Describe(true, true, "ok").State, chosen.Telemetry);
    }

    [Fact]
    public void Select_UsesOcrWhenMemoryIsNotUsable()
    {
        GameTimerReading chosen = FallbackGameTimer.Select(
            new GameTimerReading(false, "memory", "near-zero", null, 100, 1f / 4096f),
            new GameTimerReading(true, "ocr", "ok", TimeSpan.FromSeconds(8))
        );

        Assert.True(chosen.Ok);
        Assert.Equal("ocr", chosen.Source);
        Assert.Equal("near-zero", chosen.Reason);
        Assert.Equal(TimeSpan.FromSeconds(8), chosen.Time);
        Assert.Equal(ClockTelemetry.Describe(true, false, "near-zero").State, chosen.Telemetry);
    }

    [Fact]
    public void Select_UsesOcrWhenMemoryTimeIsOutsideAMatch()
    {
        GameTimerReading chosen = FallbackGameTimer.Select(
            new GameTimerReading(true, "memory", "ok", TimeSpan.FromHours(30)),
            new GameTimerReading(true, "ocr", "ok", TimeSpan.FromSeconds(40))
        );

        Assert.Equal("ocr", chosen.Source);
        Assert.Equal("memory-unplayable", chosen.Reason);
        Assert.Equal(TimeSpan.FromSeconds(40), chosen.Time);
    }
}
