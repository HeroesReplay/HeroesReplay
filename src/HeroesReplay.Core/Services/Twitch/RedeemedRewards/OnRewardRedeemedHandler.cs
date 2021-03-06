﻿using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.Logging;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RedeemedRewards
{
    public class OnRewardRedeemedHandler : IOnRewardHandler
    {
        private readonly ICustomRewardsHolder rewards;
        private readonly ILogger<OnRewardRedeemedHandler> logger;
        public readonly IEnumerable<IRewardHandler> handlers;

        public OnRewardRedeemedHandler(ILogger<OnRewardRedeemedHandler> logger, IEnumerable<IRewardHandler> handlers, ICustomRewardsHolder rewards)
        {
            this.logger = logger;
            this.handlers = handlers;
            this.rewards = rewards;
        }

        public void Handle(OnRewardRedeemedArgs args)
        {
            if (rewards.TryGetReward(args, out SupportedReward reward))
            {
                foreach (var handler in handlers.Where(handler => handler.Supports.Contains(reward.RewardType)))
                {
                    try
                    {
                        handler.Execute(reward, args);
                    }
                    catch (Exception e) when (handler != null)
                    {
                        logger.LogError(e, $"Could not execute handler: {handler.GetType().Name}");
                    }
                }
            }
            else
            {
                logger.LogWarning($"Could not handle reward '{args.RewardTitle}' because it was not found");
            }
        }
    }
}
