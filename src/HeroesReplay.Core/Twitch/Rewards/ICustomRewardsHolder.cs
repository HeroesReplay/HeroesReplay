using System.Collections.Generic;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Twitch.Rewards;

public interface ICustomRewardsHolder
{
    public List<SupportedReward> Rewards { get; }
    public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward);
}
