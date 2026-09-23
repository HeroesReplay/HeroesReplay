using System;
using System.Collections.Generic;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.RedeemedRewards;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.Logging.Abstractions;
using TwitchLib.PubSub.Events;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class OnRewardRedeemedHandlerTests
{
    [Fact]
    public void Handle_SameRedemption_RunsOnce()
    {
        var counter = new CountingHandler();
        var handler = new OnRewardRedeemedHandler(
            NullLogger<OnRewardRedeemedHandler>.Instance,
            new IRewardHandler[] { counter },
            new FixedRewards()
        );
        var args = new OnRewardRedeemedArgs
        {
            RewardTitle = "Braxis Holdout (Rank SL)",
            RedemptionId = Guid.NewGuid(),
            Login = "saltysadism",
            Message = "gold",
        };

        handler.Handle(args);
        handler.Handle(args);

        Assert.Equal(1, counter.Calls);
    }

    [Fact]
    public void Handle_EmptyRedemptionId_IsNotCollapsed()
    {
        var counter = new CountingHandler();
        var handler = new OnRewardRedeemedHandler(
            NullLogger<OnRewardRedeemedHandler>.Instance,
            new IRewardHandler[] { counter },
            new FixedRewards()
        );
        var args = new OnRewardRedeemedArgs
        {
            RewardTitle = "Braxis Holdout (Rank SL)",
            Login = "saltysadism",
        };

        handler.Handle(args);
        handler.Handle(args);

        Assert.Equal(2, counter.Calls);
    }

    [Fact]
    public void Handle_DuplicateUnknownReward_DoesNotLookUpTwice()
    {
        var rewards = new CountingRewards();
        var handler = new OnRewardRedeemedHandler(
            NullLogger<OnRewardRedeemedHandler>.Instance,
            Array.Empty<IRewardHandler>(),
            rewards
        );
        var args = new OnRewardRedeemedArgs
        {
            RewardTitle = "missing",
            RedemptionId = Guid.NewGuid(),
        };

        handler.Handle(args);
        handler.Handle(args);

        Assert.Equal(1, rewards.Lookups);
    }

    private sealed class FixedRewards : ICustomRewardsHolder
    {
        public List<SupportedReward> Rewards { get; } = new();

        public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward)
        {
            reward = new SupportedReward(
                RewardType.SL | RewardType.Map | RewardType.Rank,
                args.RewardTitle,
                "Braxis Holdout",
                GameType.StormLeague,
                1000
            );
            return true;
        }
    }

    private sealed class CountingRewards : ICustomRewardsHolder
    {
        public int Lookups { get; private set; }

        public List<SupportedReward> Rewards { get; } = new();

        public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward)
        {
            Lookups++;
            reward = null;
            return false;
        }
    }

    private sealed class CountingHandler : IRewardHandler
    {
        public int Calls { get; private set; }

        public IEnumerable<RewardType> Supports { get; } =
            new[] { RewardType.SL | RewardType.Map | RewardType.Rank };

        public void Execute(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            Calls++;
        }
    }
}
