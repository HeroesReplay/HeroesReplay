using HeroesReplay.Core.Requests;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Twitch.Rewards;

public interface IRewardRequestFactory
{
    RewardRequest Create(SupportedReward reward, OnRewardRedeemedArgs args);
}
