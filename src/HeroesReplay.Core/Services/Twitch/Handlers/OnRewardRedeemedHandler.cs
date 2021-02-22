
using System;
using System.Collections.Generic;
using System.Linq;

using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RewardHandlers
{
    public class OnRewardRedeemedHandler : IOnRewardRedeemedHandler
    {
        private readonly ISupportedRewardsHolder rewards;
        public readonly IEnumerable<IRewardHandler> handlers;

        public OnRewardRedeemedHandler(IEnumerable<IRewardHandler> handlers, ISupportedRewardsHolder rewards)
        {
            this.handlers = handlers;
            this.rewards = rewards;
        }

        public void Handle(OnRewardRedeemedArgs rewardRedeemed)
        {
            if (rewards.TryGetReward(rewardRedeemed, out SupportedReward reward))
            {
                foreach (var handler in handlers.Where(handler => handler.Supports.Contains(reward.RewardType)))
                {
                    try
                    {
                        handler.Execute(reward, rewardRedeemed);
                    }
                    catch (Exception e)
                    {
                        // Log error with handler execution
                    }
                }
            }
            else
            {
                // log reward type not found
            }
        }
    }
}
