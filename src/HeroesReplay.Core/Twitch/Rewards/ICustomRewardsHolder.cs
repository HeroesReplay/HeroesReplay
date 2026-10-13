using System;
using System.Collections.Generic;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Twitch.Rewards;

public interface ICustomRewardsHolder
{
    public List<SupportedReward> Rewards { get; }
    public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward);

    /// <summary>
    /// Reward titles this app created that the catalog no longer offers and that can never be
    /// fulfilled, such as the Storm League rewards of a map out of the pool (#425). The sync
    /// deletes them from the channel.
    /// </summary>
    public IReadOnlyList<string> RetiredTitles => Array.Empty<string>();

    /// <summary>One log line on the Storm League map pool the catalog used, or null.</summary>
    public string PoolSummary => null;
}
