using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.Rewards
{
    public class SupportedRewardsHolder : ICustomRewardsHolder
    {
        private readonly IGameData gameData;

        private List<SupportedReward> rewards;

        public List<SupportedReward> Rewards => rewards ??= GetRewards();

        public SupportedRewardsHolder(IGameData gameData)
        {
            this.gameData = gameData;
        }

        private List<SupportedReward> GetRewards()
        {

            //List<GameRank> tiers = Enum.GetValues(typeof(GameRank))
            //                           .OfType<GameRank>()
            //                           .ToList();

            var rewards = new List<SupportedReward>();

            var rankedMaps = gameData.Maps.Where(m => m.Playable && m.RankedRotation);
            var unrankedMaps = gameData.Maps.Where(m => m.Playable && !m.RankedRotation && m.Type.Equals("standard"));
            var aramMaps = gameData.Maps.Where(m => m.Playable && m.Type.Equals("ARAM"));

            rewards.Add(new SupportedReward(RewardType.QM, "Random (QM)", mode: GameType.QuickMatch, cost: 250));
            rewards.Add(new SupportedReward(RewardType.SL, "Random (SL)", mode: GameType.StormLeague, cost: 250));
            rewards.Add(new SupportedReward(RewardType.UD, "Random (UD)", mode: GameType.UnrankedDraft, cost: 250));
            rewards.Add(new SupportedReward(RewardType.ARAM, "Random (ARAM)", mode: GameType.ARAM, cost: 250));

            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.QM | RewardType.Map, $"{map.Name} (QM)", map.Name, GameType.QuickMatch, 500)));
            rewards.AddRange(rankedMaps.Select(map => new SupportedReward(RewardType.SL | RewardType.Map, $"{map.Name} (SL)", map.Name, GameType.StormLeague, 500)));
            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.UD | RewardType.Map, $"{map.Name} (UD)", map.Name, GameType.UnrankedDraft, 500)));
            rewards.AddRange(aramMaps.Select(map => new SupportedReward(RewardType.ARAM | RewardType.Map, $"{map.Name} (ARAM)", map.Name, GameType.ARAM, 500)));

            // rewards.Add(new SupportedReward(RewardType.QM | RewardType.Rank, $"Rank (QM)", mode: GameType.QuickMatch, cost: 750));
            // rewards.Add(new SupportedReward(RewardType.SL | RewardType.Rank, $"Rank (SL)", mode: GameType.StormLeague, cost: 750));
            // rewards.Add(new SupportedReward(RewardType.UD | RewardType.Rank, $"Rank (UD)", mode: GameType.UnrankedDraft, cost: 750));
            // rewards.Add(new SupportedReward(RewardType.ARAM | RewardType.Rank, $"Rank (ARAM)", mode: GameType.ARAM, cost: 750));

            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.QM | RewardType.Map | RewardType.Rank, $"{map.Name} (Rank QM)", map.Name, GameType.QuickMatch, 1000)));
            rewards.AddRange(rankedMaps.Select(map => new SupportedReward(RewardType.SL | RewardType.Map | RewardType.Rank, $"{map.Name} (Rank SL)", map.Name, GameType.StormLeague, 1000)));
            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.UD | RewardType.Map | RewardType.Rank, $"{map.Name} (Rank UD)", map.Name, GameType.UnrankedDraft, 1000)));
            rewards.AddRange(aramMaps.Select(map => new SupportedReward(RewardType.ARAM | RewardType.Map | RewardType.Rank, $"{map.Name} (Rank ARAM)", map.Name, GameType.ARAM, 1000)));

            rewards.Add(new SupportedReward(RewardType.ReplayId, nameof(RewardType.ReplayId), cost: 500));

            return rewards;
        }

        public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward)
        {
            reward = null;

            foreach (var item in Rewards)
            {
                if (item.Title == args.RewardTitle)
                {
                    reward = item;
                    return true;
                }
            }

            return false;
        }
    }
}
