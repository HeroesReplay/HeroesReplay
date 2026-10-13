using HeroesReplay.Core.Requests;
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

        if (reward.RewardType.HasFlag(RewardType.Rank))
        {
            // "Gold", "gold 3", "Plat" and "GM" all name a league (#429). Text that names none
            // leaves Rank empty, and the queue returns the points instead of queueing any game.
            return new RewardRequest(
                args.Login,
                args.RedemptionId,
                reward.Title,
                replayId: null,
                rank: RankInput.Read(args.Message),
                reward.Map,
                reward.Mode
            )
            {
                RankRequired = true,
            };
        }

        return new RewardRequest(
            args.Login,
            args.RedemptionId,
            reward.Title,
            replayId: null,
            rank: null,
            reward.Map,
            reward.Mode
        )
        {
            Blind = reward.RewardType.HasFlag(RewardType.Blind),
        };
    }
}
