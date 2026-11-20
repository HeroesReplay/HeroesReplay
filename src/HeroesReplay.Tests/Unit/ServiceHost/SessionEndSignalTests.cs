using System;
using HeroesReplay.Core.ServiceHost;
using Xunit;

namespace HeroesReplay.Tests.Unit.ServiceHost;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SessionEndSignalTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 3, 30, 2, TimeSpan.Zero);

    [Fact]
    public void Unmarked_IsNotEnding()
    {
        Assert.Null(new SessionEndSignal().Ending(At));
    }

    [Fact]
    public void Marked_IsEndingUntilTheCancelWindowPasses()
    {
        var signal = new SessionEndSignal();
        signal.Mark("SystemShutdown", At);

        Assert.Equal("SystemShutdown since 03:30:02Z", signal.Ending(At.AddSeconds(5)));
        Assert.NotNull(signal.Ending(At + SessionEndSignal.CancelledAfter));
        Assert.Null(signal.Ending(At + SessionEndSignal.CancelledAfter + TimeSpan.FromSeconds(1)));
    }
}
