using System;
using HeroesReplay.Core.Models;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.Rewards;

public class RewardRequestFactory : IRewardRequestFactory
{
    public RewardRequest Create(SupportedReward reward, OnRewardRedeemedArgs args)
    {
        if (
            reward.RewardType == RewardType.ReplayId
            && PlayerPriorityRequest.TryRead(args.Message, out int replayId, out int? playerIndex)
        )
        {
            return new RewardRequest(
                args.Login,
                args.RedemptionId,
                reward.Title,
                replayId,
                rank: null,
                reward.Map,
                reward.Mode
            )
            {
                RecordAndUpload = reward.RecordAndUpload,
                PlayerIndex = playerIndex,
            };
        }
        else
        {
            if (
                reward.RewardType.HasFlag(RewardType.Rank)
                && Enum.TryParse(args.Message, ignoreCase: true, out GameRank rank)
            )
            {
                return new RewardRequest(
                    args.Login,
                    args.RedemptionId,
                    reward.Title,
                    replayId: null,
                    rank: rank,
                    reward.Map,
                    reward.Mode
                );
            }
            else
            {
                return new RewardRequest(
                    args.Login,
                    args.RedemptionId,
                    reward.Title,
                    replayId: null,
                    rank: null,
                    reward.Map,
                    reward.Mode
                );
            }
        }

        throw new NotSupportedException();
    }
}
