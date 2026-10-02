using System.Threading;
using HeroesReplay.Core.Spectating.Clock;
using HeroesReplay.Core.Spectating.Clock.Memory;
using HeroesReplay.Core.Spectating.Control;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Clock;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class StableGameTimerTests
{
    [Fact]
    public void ReadAsync_WithoutAGameProcess_HasNoClockAndNeverFallsBackToTheScreen()
    {
        var timer = new StableGameTimer(new StubController(NullLogger<StubController>.Instance));

        GameTimerReading reading = timer.ReadAsync(CancellationToken.None).Result;

        Assert.False(reading.Ok);
        Assert.Null(reading.Time);
        Assert.Equal("memory", reading.Source);
        Assert.Equal("no-process", reading.Reason);
    }
}
