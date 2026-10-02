using HeroesReplay.Core.Requests;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PlayerPriorityRequestTests
{
    [Theory]
    [InlineData("65268119", 65268119, null)]
    [InlineData("65268119,1", 65268119, 0)]
    [InlineData("65268119, 0", 65268119, 9)]
    [InlineData("65268119,9", 65268119, 8)]
    public void TryRead_ParsesReplayAndObserveSlot(string message, int replayId, int? playerIndex)
    {
        Assert.True(PlayerPriorityRequest.TryRead(message, out int id, out int? slot));
        Assert.Equal(replayId, id);
        Assert.Equal(playerIndex, slot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("65268119,10")]
    [InlineData("65268119,player")]
    public void TryRead_RejectsAnythingExceptReplayOrReplayAndDigit(string message)
    {
        Assert.False(PlayerPriorityRequest.TryRead(message, out _, out _));
    }

    [Fact]
    public void BlocksBecauseMatchStarted_OnlyForTheReplayAlreadyOnScreen()
    {
        Assert.True(PlayerPriorityRequest.BlocksBecauseMatchStarted(10, "TimerDetected", 10));
        Assert.True(PlayerPriorityRequest.BlocksBecauseMatchStarted(10, "Loading", 10));
        Assert.False(PlayerPriorityRequest.BlocksBecauseMatchStarted(10, "Idle", 10));
        Assert.False(PlayerPriorityRequest.BlocksBecauseMatchStarted(10, "TimerDetected", 11));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Watch_FollowsTheHeroWheneverTheyAreAlive(bool alive, bool watch)
    {
        Assert.Equal(watch, PlayerPriorityRequest.Watch(alive));
    }
}
