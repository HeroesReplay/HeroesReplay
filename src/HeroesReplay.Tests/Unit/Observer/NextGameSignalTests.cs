using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class NextGameSignalTests
{
    [Fact]
    public async Task DelayAsync_ReturnsWhenTheNextGameIsDetected()
    {
        var signal = new NextGameSignal();
        Task delay = signal.DelayAsync(TimeSpan.FromSeconds(30));
        signal.Signal();
        Task finished = await Task.WhenAny(delay, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(delay, finished);
        Assert.True(signal.IsSignaled);
    }
}
