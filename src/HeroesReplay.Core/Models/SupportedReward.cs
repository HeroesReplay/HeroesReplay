namespace HeroesReplay.Core.Models
{
    public class SupportedReward
    {
        public RewardType RewardType { get; set; }
        public string Title { get; set; }
        public string Map { get; set; }
        public GameType? Mode { get; set; }
        public string BackgroundColor { get; set; }
        public bool ShouldRedemptionsSkipRequestQueue { get; set; }
        public bool IsUserInputRequired { get; set; }
        public string Prompt { get; set; }
        public int Cost { get; set; }

        public SupportedReward(RewardType rewardType, string title, string map = null, GameType? mode = null, int cost = 0)
        {
            Prompt = rewardType.HasFlag(RewardType.Rank) ? "Enter a rank without a division" : null;
            IsUserInputRequired = rewardType.HasFlag(RewardType.Rank) || rewardType == RewardType.ReplayId;
            RewardType = rewardType;
            Title = title;
            Map = map;
            Mode = mode;
            Cost = cost;
        }
    }
}
