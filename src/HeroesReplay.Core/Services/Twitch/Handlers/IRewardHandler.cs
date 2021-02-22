using System.Collections.Generic;

using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public interface IRewardHandler
    {
        IEnumerable<RewardType> Supports { get; }
        void Execute(SupportedReward reward, OnRewardRedeemedArgs args);
    }
}
