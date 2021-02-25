
using System;

namespace HeroesReplay.Core.Models
{
    public class RewardRequest
    {
        public Guid RedemptionId { get; set; }
        public string RewardTitle { get; set; }
        public string Login { get; set; }
        public int? ReplayId { get; set; }
        public GameRank? Rank { get; set; }
        public string Map { get; set; }
        public GameType? GameType { get; set; }

        public RewardRequest()
        {

        }

        public RewardRequest(string login, Guid redemptionId, string rewardTitle, int? replayId, GameRank? rank, string map, GameType? gameMode)
        {
            RedemptionId = redemptionId;
            Login = login;
            RewardTitle = rewardTitle;
            ReplayId = replayId;
            Rank = rank;
            Map = map;
            GameType = gameMode;
        }
    }
}
