using HeroesReplay.Core.Services.Providers;
using Xunit;

namespace HeroesReplay.Tests.Unit.Providers;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SpectateQueueTests
{
    [Fact]
    public void CountWaiting_SkipsBelowFloorQuarantineAndDeferred()
    {
        int[] onDisk = { 100, 200, 300, 400, 500 };
        int[] notWaiting = { 200, 300, 400 };

        Assert.Equal(2, SpectateQueue.CountWaiting(onDisk, notWaiting, stopAt: 5));
    }

    [Fact]
    public void CountWaiting_StopsAtTheDownloadLimit()
    {
        int[] onDisk = { 1, 2, 3, 4, 5 };

        Assert.Equal(2, SpectateQueue.CountWaiting(onDisk, notWaiting: null, stopAt: 2));
    }

    [Fact]
    public void CountWaiting_IsEmptyWhenEveryFileIsAlreadySettled()
    {
        int[] onDisk = { 10, 11 };
        int[] notWaiting = { 10, 11 };

        Assert.Equal(0, SpectateQueue.CountWaiting(onDisk, notWaiting, stopAt: 5));
        Assert.Equal(0, SpectateQueue.CountWaiting(onDisk: null, notWaiting, stopAt: 5));
    }

    [Fact]
    public void TryParseId_ReadsADeferredLineAndASpectatedLine()
    {
        Assert.True(SpectateQueue.TryParseId("65389750 1790759492", out int deferred));
        Assert.Equal(65389750, deferred);
        Assert.True(SpectateQueue.TryParseId("65540173", out int spectated));
        Assert.Equal(65540173, spectated);
        Assert.False(SpectateQueue.TryParseId("   ", out _));
        Assert.False(SpectateQueue.TryParseId("not-an-id 12", out _));
    }
}
