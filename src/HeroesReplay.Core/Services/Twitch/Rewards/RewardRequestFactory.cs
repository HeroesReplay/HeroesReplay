using HeroesReplay.Core.Models;

using System;

using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.Rewards
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
                if (reward.RewardType.HasFlag(RewardType.Rank) && Enum.TryParse(args.Message, out GameRank rank))
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
