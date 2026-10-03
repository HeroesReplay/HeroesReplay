using System;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Twitch.Rewards;

public class RewardRequestFactory : IRewardRequestFactory
{
    public RewardRequest Create(SupportedReward reward, OnRewardRedeemedArgs args)
    {
        RewardRequest request = Build(reward, args);
        request.RewardId = args.RewardId;
        request.BroadcasterId = args.ChannelId;
        return request;
    }

    private static RewardRequest Build(SupportedReward reward, OnRewardRedeemedArgs args)
    {
        if (
            reward.RewardType == RewardType.ReplayId
            && PlayerPriorityRequest.TryRead(args.Message, out int replayId, out string battleTag)
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
                BattleTag = battleTag,
            };
        }

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
