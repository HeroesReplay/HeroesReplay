using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

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
    }
}
