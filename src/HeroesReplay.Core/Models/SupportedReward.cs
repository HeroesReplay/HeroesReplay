namespace HeroesReplay.Core.Models
{
    public class SupportedReward
    {
        public RewardType RewardType { get; }
        public string Title { get; }
        public string Map { get; }
        public GameRank? Rank { get; }
        public GameType? Mode { get; }

        public SupportedReward(RewardType rewardType, string title, string map = null, GameRank? tier = null, GameType? mode = null)
        {
            RewardType = rewardType;
            Title = title;
            Map = map;
            Rank = tier;
            Mode = mode;
        }
    }
}
