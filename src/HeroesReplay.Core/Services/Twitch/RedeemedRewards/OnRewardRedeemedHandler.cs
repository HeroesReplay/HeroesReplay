using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch.Rewards;
using Microsoft.Extensions.Logging;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.RedeemedRewards;

public class OnRewardRedeemedHandler : IOnRewardHandler
{
    private const int SeenRedemptionLimit = 2000;

    private readonly ICustomRewardsHolder rewards;
    private readonly ILogger<OnRewardRedeemedHandler> logger;
    private readonly HashSet<Guid> seenRedemptions = new();
    private readonly Queue<Guid> seenOrder = new();
    private readonly object seenGate = new();
    public readonly IEnumerable<IRewardHandler> handlers;

    public OnRewardRedeemedHandler(
        ILogger<OnRewardRedeemedHandler> logger,
        IEnumerable<IRewardHandler> handlers,
        ICustomRewardsHolder rewards
    )
    {
        this.logger = logger;
        this.handlers = handlers;
        this.rewards = rewards;
    }

    public void Handle(OnRewardRedeemedArgs args)
    {
        if (args == null)
        {
            return;
        }

        if (args.RedemptionId != Guid.Empty && !IsFirstDelivery(args.RedemptionId))
        {
            logger.LogInformation(
                "Ignoring duplicate redemption {RedemptionId} for '{Title}'.",
                args.RedemptionId,
                args.RewardTitle
            );
            return;
        }

        if (rewards.TryGetReward(args, out SupportedReward reward))
        {
            foreach (
                var handler in handlers.Where(handler =>
                    handler.Supports.Contains(reward.RewardType)
                )
            )
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
            logger.LogWarning(
                $"Could not handle reward '{args.RewardTitle}' because it was not found"
            );
        }
    }

    private bool IsFirstDelivery(Guid redemptionId)
    {
        lock (seenGate)
        {
            if (!seenRedemptions.Add(redemptionId))
            {
                return false;
            }

            seenOrder.Enqueue(redemptionId);
            while (seenOrder.Count > SeenRedemptionLimit)
            {
                seenRedemptions.Remove(seenOrder.Dequeue());
            }

            return true;
        }
    }
}
