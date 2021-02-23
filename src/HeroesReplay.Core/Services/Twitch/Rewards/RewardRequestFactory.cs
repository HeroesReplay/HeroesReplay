using HeroesReplay.Core.Models;

using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch
{
    public class RewardRequestFactory : IRewardRequestFactory
    {
        public RewardRequest Create(SupportedReward reward, OnRewardRedeemedArgs args)
        {
            switch (reward.RewardType)
            {
                case RewardType.ReplayId when !string.IsNullOrWhiteSpace(args.Message) && int.TryParse(args.Message.Trim(), out int replayId):
                    return new RewardRequest(args.Login, args.RedemptionId, reward.RewardTitle, replayId, reward.Tier, reward.Map, reward.Mode);
                default:
                    return new RewardRequest(args.Login, args.RedemptionId, reward.RewardTitle, replayId: null, reward.Tier, reward.Map, reward.Mode);
            }
        }
    }
}
