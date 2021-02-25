using HeroesReplay.Core.Models;

using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch
{
    public class RewardRequestFactory : IRewardRequestFactory
    {
        public RewardRequest Create(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            return reward.RewardType switch
            {
                RewardType.ReplayId when !string.IsNullOrWhiteSpace(args.Message) && int.TryParse(args.Message.Trim(), out int replayId) => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId, reward.Rank, reward.Map, reward.Mode),
                RewardType.QM => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.SL => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.UD => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.ARAM => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.QMMap => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.UDMap => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.SLMap => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.ARAMMap => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.QMTier => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.UDTier => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.SLTier => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.ARAMTier => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.QMMapTier => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.UDMapTier => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.SLMapTier => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                RewardType.ARAMMapTier => new RewardRequest(args.Login, args.RedemptionId, reward.Title, replayId: null, reward.Rank, reward.Map, reward.Mode),
                _ => throw new System.NotImplementedException()
            };
        }
    }
}
