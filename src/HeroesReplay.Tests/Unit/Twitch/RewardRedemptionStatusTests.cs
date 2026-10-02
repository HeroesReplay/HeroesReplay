using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Spectating;
using HeroesReplay.Core.Twitch.RedeemedRewards;
using HeroesReplay.Core.Twitch.Rewards;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RewardRedemptionStatusTests
{
    [Fact]
    public void Decide_Verified_IsFulfilled()
    {
        Assert.Equal(
            RewardRedemptionStatus.Fulfilled,
            RewardRedemptionStatus.Decide(RewardTerminal.Verified)
        );
    }

    [Fact]
    public void Decide_InvalidInput_IsCanceled()
    {
        Assert.Equal(
            RewardRedemptionStatus.Canceled,
            RewardRedemptionStatus.Decide(RewardTerminal.InvalidInput)
        );
    }

    [Fact]
    public void Decide_UnavailableReplay_IsCanceled()
    {
        Assert.Equal(
            RewardRedemptionStatus.Canceled,
            RewardRedemptionStatus.Decide(RewardTerminal.UnavailableReplay)
        );
    }

    [Fact]
    public void Decide_Duplicate_IsCanceled()
    {
        Assert.Equal(
            RewardRedemptionStatus.Canceled,
            RewardRedemptionStatus.Decide(RewardTerminal.Duplicate)
        );
    }

    [Fact]
    public void Decide_QueueFailure_IsCanceled()
    {
        Assert.Equal(
            RewardRedemptionStatus.Canceled,
            RewardRedemptionStatus.Decide(RewardTerminal.QueueFailure)
        );
        Assert.Equal(
            RewardRedemptionStatus.Canceled,
            RewardRedemptionStatus.ForQueue(new RewardResponse(false, "error"))
        );
        Assert.Equal(RewardRedemptionStatus.Canceled, RewardRedemptionStatus.ForQueue(null));
    }

    [Fact]
    public void Decide_LaunchFailure_IsCanceled()
    {
        Assert.Equal(
            RewardRedemptionStatus.Canceled,
            RewardRedemptionStatus.Decide(RewardTerminal.LaunchFailure)
        );
        Assert.Equal(
            RewardTerminal.LaunchFailure,
            RewardRedemptionStatus.FromOutcome(MatchOutcome.LoadTimedOut)
        );
    }

    [Fact]
    public void Decide_SpectateFailure_IsCanceled()
    {
        Assert.Equal(
            RewardRedemptionStatus.Canceled,
            RewardRedemptionStatus.Decide(RewardTerminal.SpectateFailure)
        );
        Assert.Equal(
            RewardTerminal.SpectateFailure,
            RewardRedemptionStatus.FromOutcome(MatchOutcome.ClientHung)
        );
        Assert.Equal(
            RewardTerminal.Verified,
            RewardRedemptionStatus.FromOutcome(MatchOutcome.VerifiedCompleted)
        );
    }

    [Fact]
    public void Handle_RecordsInvalidDuplicateAndUnavailableWithoutCallingTwitch()
    {
        var rewards = new MissingRewards();
        var handler = new OnRewardRedeemedHandler(
            NullLogger<OnRewardRedeemedHandler>.Instance,
            System.Array.Empty<IRewardHandler>(),
            rewards
        );

        handler.Handle(null);
        Assert.Equal(
            RewardRedemptionStatus.Decide(RewardTerminal.InvalidInput),
            handler.RecordedStatus
        );

        var args = new TwitchLib.PubSub.Events.OnRewardRedeemedArgs
        {
            RewardTitle = "missing",
            RedemptionId = System.Guid.NewGuid(),
        };
        handler.Handle(args);
        Assert.Equal(
            RewardRedemptionStatus.Decide(RewardTerminal.UnavailableReplay),
            handler.RecordedStatus
        );

        handler.Handle(args);
        Assert.Equal(
            RewardRedemptionStatus.Decide(RewardTerminal.Duplicate),
            handler.RecordedStatus
        );
        Assert.Equal(1, rewards.Lookups);
    }

    private sealed class MissingRewards : ICustomRewardsHolder
    {
        public int Lookups { get; private set; }

        public System.Collections.Generic.List<SupportedReward> Rewards { get; } = new();

        public bool TryGetReward(
            TwitchLib.PubSub.Events.OnRewardRedeemedArgs args,
            out SupportedReward reward
        )
        {
            Lookups++;
            reward = null;
            return false;
        }
    }
}
