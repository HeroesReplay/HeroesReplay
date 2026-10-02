using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Twitch.Rewards;
using TwitchLib.PubSub.Events;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch.Rewards;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RewardRequestFactoryTests
{
    [Fact]
    public void ReplayId_CopiesRecordAndUpload()
    {
        var factory = new RewardRequestFactory();
        var reward = new SupportedReward(
            RewardType.ReplayId,
            "ReplayId + YouTube",
            cost: 1000,
            recordAndUpload: true
        );
        var args = new OnRewardRedeemedArgs
        {
            Login = "viewer",
            RedemptionId = System.Guid.NewGuid(),
            RewardTitle = reward.Title,
            Message = "65268119",
        };

        RewardRequest request = factory.Create(reward, args);

        Assert.Equal(65268119, request.ReplayId);
        Assert.True(request.RecordAndUpload);
        Assert.Equal("ReplayId + YouTube", request.RewardTitle);
    }

    [Fact]
    public void ReplayId_PlainRewardKeepsItsFlagAndStillUploads()
    {
        var factory = new RewardRequestFactory();
        var reward = new SupportedReward(RewardType.ReplayId, "ReplayId", cost: 500);
        var args = new OnRewardRedeemedArgs
        {
            Login = "viewer",
            RedemptionId = System.Guid.NewGuid(),
            RewardTitle = reward.Title,
            Message = "65268119",
        };

        RewardRequest request = factory.Create(reward, args);

        Assert.Equal(65268119, request.ReplayId);
        Assert.False(request.RecordAndUpload);
        Assert.True(ReplayRequestKind.RecordsAndUploads(request));
        Assert.Null(request.PlayerIndex);
    }

    [Fact]
    public void Create_KeepsTheRewardAndChannelForFulfilment()
    {
        var factory = new RewardRequestFactory();
        var rewardId = System.Guid.NewGuid();
        var args = new OnRewardRedeemedArgs
        {
            Login = "viewer",
            ChannelId = "123456",
            RewardId = rewardId,
            RedemptionId = System.Guid.NewGuid(),
            RewardTitle = "ReplayId",
            Message = "65268119",
        };

        RewardRequest replayId = factory.Create(
            new SupportedReward(RewardType.ReplayId, "ReplayId", cost: 250),
            args
        );
        RewardRequest map = factory.Create(
            new SupportedReward(RewardType.ARAM, "Random (ARAM)", cost: 125),
            args
        );

        Assert.Equal(rewardId, replayId.RewardId);
        Assert.Equal("123456", replayId.BroadcasterId);
        Assert.Equal(rewardId, map.RewardId);
        Assert.Equal("123456", map.BroadcasterId);
        Assert.False(ReplayRequestKind.RecordsAndUploads(map));
    }

    [Fact]
    public void ReplayId_WithPlayerDigit_SetsTheObserveSlot()
    {
        var factory = new RewardRequestFactory();
        var reward = new SupportedReward(RewardType.ReplayId, "ReplayId", cost: 500);
        var args = new OnRewardRedeemedArgs
        {
            Login = "viewer",
            RedemptionId = System.Guid.NewGuid(),
            RewardTitle = reward.Title,
            Message = "65268119,0",
        };

        RewardRequest request = factory.Create(reward, args);

        Assert.Equal(65268119, request.ReplayId);
        Assert.Equal(9, request.PlayerIndex);
    }
}
