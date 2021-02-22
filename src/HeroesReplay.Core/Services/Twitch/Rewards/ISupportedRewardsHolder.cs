
using System.Collections.Generic;

using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch
{
    public interface ISupportedRewardsHolder
    {
        public List<SupportedReward> Rewards { get; }
        public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward);
    }
}
