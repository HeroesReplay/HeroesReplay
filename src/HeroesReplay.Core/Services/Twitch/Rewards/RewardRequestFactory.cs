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
                RewardType.ReplayId when !string.IsNullOrWhiteSpace(args.Message) && int.TryParse(args.Message.Trim(), out int replayId) => new RewardRequest(args.Login, args.RedemptionId, reward.RewardTitle, replayId, reward.Tier, reward.Map, reward.Mode),
                _ => new RewardRequest(args.Login, args.RedemptionId, reward.RewardTitle, replayId: null, reward.Tier, reward.Map, reward.Mode),
            };
        }
    }
}
