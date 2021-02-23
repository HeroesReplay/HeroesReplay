
using System;

namespace HeroesReplay.Core.Models
{
    public class RewardRequest
    {
        public Guid RedemptionId { get; set; }
        public string RewardTitle { get; set; }
        public string Login { get; set; }
        public int? ReplayId { get; set; }
        public Tier? Tier { get; set; }
        public string Map { get; set; }
        public GameMode? GameMode { get; set; }

        public RewardRequest(string login, Guid redemptionId, string rewardTitle, int? replayId, Tier? tier, string map, GameMode? gameMode)
        {
            RedemptionId = redemptionId;
            Login = login;
            RewardTitle = rewardTitle;
            ReplayId = replayId;
            Tier = tier;
            Map = map;
            GameMode = gameMode;
        }
    }
}
