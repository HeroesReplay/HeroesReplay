using System.Collections.Generic;
using HeroesReplay.Core.Replays;
using Xunit;

namespace HeroesReplay.Tests.Unit.Replays;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class SpectateQueueTests
{
    [Fact]
    public void CountWaiting_SkipsBelowFloorQuarantineAndDeferred()
    {
        int[] onDisk = { 100, 200, 300, 400, 500 };
        int[] notWaiting = { 200, 300, 400 };

        Assert.Equal(
            new WaitingReplays(Waiting: 2, Fresh: 2),
            SpectateQueue.CountWaiting(onDisk, notWaiting, isFresh: null)
        );
    }

    /// <summary>#280: every waiting replay is counted, and only the fresh ones are fresh.</summary>
    [Fact]
    public void CountWaiting_CountsEveryWaitingReplayAndTheFreshOnes()
    {
        int[] onDisk = { 1, 2, 3, 4, 5, 6, 7 };
        int[] notWaiting = { 7 };

        WaitingReplays waiting = SpectateQueue.CountWaiting(onDisk, notWaiting, id => id > 4);

        Assert.Equal(new WaitingReplays(Waiting: 6, Fresh: 2), waiting);
    }

    [Fact]
    public void CountWaiting_DoesNotJudgeASettledReplay()
    {
        int[] onDisk = { 10, 11, 12 };
        int[] notWaiting = { 11 };
        var judged = new List<int>();

        SpectateQueue.CountWaiting(
            onDisk,
            notWaiting,
            id =>
            {
                judged.Add(id);
                return true;
            }
        );

        Assert.Equal(new[] { 10, 12 }, judged);
    }

    [Fact]
    public void CountWaiting_IsEmptyWhenEveryFileIsAlreadySettled()
    {
        int[] onDisk = { 10, 11 };
        int[] notWaiting = { 10, 11 };

        var none = new WaitingReplays(Waiting: 0, Fresh: 0);
        Assert.Equal(none, SpectateQueue.CountWaiting(onDisk, notWaiting, isFresh: null));
        Assert.Equal(none, SpectateQueue.CountWaiting(onDisk: null, notWaiting, id => true));
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
