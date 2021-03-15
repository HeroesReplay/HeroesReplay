using System.Collections.Generic;
using HeroesReplay.Core.Models;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Service.Twitch.Core.Rewards
{
    public interface ICustomRewardsHolder
    {
        public List<SupportedReward> Rewards { get; }
        public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward);
    }
}
