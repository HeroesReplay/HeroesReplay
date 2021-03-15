using System;
using HeroesReplay.Core.Models;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Service.Twitch.Core.Rewards
{
    public class RewardRequestFactory : IRewardRequestFactory
    {
        public RewardRequest Create(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            if (reward.RewardType == RewardType.ReplayId && !string.IsNullOrWhiteSpace(args.Message) && int.TryParse(args.Message.Trim(), out int replayId))
            {
                return new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId, rank: null, reward.Map, reward.Mode);
            }
            else
            {
                if (reward.RewardType.HasFlag(RewardType.Rank) && Enum.TryParse(args.Message, ignoreCase: true, out GameRank rank))
                {
                    return new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, rank: rank, reward.Map, reward.Mode);
                }
                else
                {
                    return new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, rank: null, reward.Map, reward.Mode);
                }
            }

            throw new NotSupportedException();
        }
    }
}
