using System.Collections.Generic;
using HeroesReplay.Core.Twitch.Rewards;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Twitch.RedeemedRewards;

public interface IRewardHandler
{
    IEnumerable<RewardType> Supports { get; }
    void Execute(SupportedReward reward, OnRewardRedeemedArgs args);
}
