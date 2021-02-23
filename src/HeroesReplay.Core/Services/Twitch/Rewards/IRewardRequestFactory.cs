using HeroesReplay.Core.Models;

using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch
{
    public interface IRewardRequestFactory
    {
        RewardRequest Create(SupportedReward reward, OnRewardRedeemedArgs args);
    }
}