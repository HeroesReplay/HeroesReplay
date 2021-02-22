using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Services.Twitch
{
    public class SupportedReward
    {
        public RewardType RewardType { get; }
        public string RewardTitle { get; }
        public string Map { get; }
        public Tier? Tier { get; }
        public GameMode? Mode { get; }

        public SupportedReward(RewardType rewardType, string title, string map = null, Tier? tier = null, GameMode? mode = null)
        {
            RewardType = rewardType;
            RewardTitle = title;
            Map = map;
            Tier = tier;
            Mode = mode;
        }
    }
}
