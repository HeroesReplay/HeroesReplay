using HeroesReplay.Core.Models;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Service.Twitch.Core.Rewards
{
    public interface IRewardRequestFactory
    {
        RewardRequest Create(SupportedReward reward, OnRewardRedeemedArgs args);
    }
}