using System.Collections.Generic;
using HeroesReplay.Core.Models;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RedeemedRewards
{
    public interface IRewardHandler
    {
        IEnumerable<RewardType> Supports { get; }
        void Execute(SupportedReward reward, OnRewardRedeemedArgs args);
    }
}
