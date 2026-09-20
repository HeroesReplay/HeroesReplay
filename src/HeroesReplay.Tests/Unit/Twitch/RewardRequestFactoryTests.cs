using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.Rewards;
using TwitchLib.PubSub.Events;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

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
    public void ReplayId_DefaultDoesNotRecordAndUpload()
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
    }
}
