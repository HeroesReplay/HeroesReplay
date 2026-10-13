using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Twitch.Rewards;

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
    public bool RecordAndUpload { get; set; }

    public SupportedReward(
        RewardType rewardType,
        string title,
        string map = null,
        GameType? mode = null,
        int cost = 0,
        bool recordAndUpload = false
    )
    {
        RecordAndUpload = recordAndUpload;
        Prompt = RewardDescription.For(rewardType, map, mode, recordAndUpload);

        IsUserInputRequired =
            rewardType.HasFlag(RewardType.Rank) || rewardType == RewardType.ReplayId;
        ShouldRedemptionsSkipRequestQueue = false;
        RewardType = rewardType;
        Title = title;
        Map = map;
        Mode = mode;
        Cost = cost;
    }
}
