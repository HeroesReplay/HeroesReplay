using System;
using System.Threading;
using Heroes.ReplayParser;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Replays.Context;
using HeroesReplay.Core.Spectating.Clock;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Clock;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class StubGameTimerTests
{
    [Fact]
    public void ReadAsync_BeforeReset_HasNoClock()
    {
        var timer = new StubGameTimer(new FixedContext(TimeSpan.FromSeconds(3)));

        GameTimerReading reading = timer.ReadAsync(CancellationToken.None).Result;

        Assert.False(reading.Ok);
    }

    [Fact]
    public void ReadAsync_TicksOneSecondPerReadToTheReplayLengthThenStops()
    {
        var timer = new StubGameTimer(new FixedContext(TimeSpan.FromSeconds(3)));
        timer.Reset();

        for (int second = 0; second <= 3; second++)
        {
            GameTimerReading reading = timer.ReadAsync(CancellationToken.None).Result;
            Assert.True(reading.Ok);
            Assert.Equal("stub", reading.Source);
            Assert.Equal(TimeSpan.FromSeconds(second), reading.Time);
        }

        Assert.False(timer.ReadAsync(CancellationToken.None).Result.Ok);

        timer.Reset();
        Assert.Equal(TimeSpan.Zero, timer.ReadAsync(CancellationToken.None).Result.Time);
    }

    private sealed class FixedContext : IReplayContext
    {
        public FixedContext(TimeSpan length)
        {
            Current = new ContextData
            {
                LoadedReplay = new LoadedReplay
                {
                    Replay = new Replay { Frames = (int)length.TotalSeconds * 16 },
                },
            };
        }

        public ContextData Previous => null;

        public ContextData Current { get; }
    }
}
