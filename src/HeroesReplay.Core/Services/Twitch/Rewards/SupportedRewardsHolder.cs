using System;
using System.Collections.Generic;
using System.Linq;

using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;

using TwitchLib.Api.Helix.Models.ChannelPoints;
using TwitchLib.Api.Helix.Models.ChannelPoints.CreateCustomReward;
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

            List<GameRank> tiers = Enum.GetValues(typeof(GameRank))
                                       .OfType<GameRank>()
                                       .ToList();

            var rewards = new List<SupportedReward>();

            var rankedMaps = gameData.Maps.Where(m => m.Playable && m.RankedRotation);
            var unrankedMaps = gameData.Maps.Where(m => m.Playable && !m.RankedRotation && m.Type.Equals("standard"));
            var aramMaps = gameData.Maps.Where(m => m.Playable && m.Type.Equals("ARAM"));

            rewards.Add(new SupportedReward(RewardType.QM, "Random (QM)", mode: GameType.QuickMatch, cost: 250));
            rewards.Add(new SupportedReward(RewardType.SL, "Random (SL)", mode: GameType.StormLeague, cost: 250));
            rewards.Add(new SupportedReward(RewardType.UD, "Random (UD)", mode: GameType.UnrankedDraft, cost: 250));
            rewards.Add(new SupportedReward(RewardType.ARAM, "Random (ARAM)", mode: GameType.ARAM, cost: 250));

            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.QM | RewardType.Map, $"{map.Name} (QM)", map.Name, mode: GameType.QuickMatch, cost: 500)));
            rewards.AddRange(rankedMaps.Select(map => new SupportedReward(RewardType.SL | RewardType.Map, $"{map.Name} (SL)", map: map.Name, mode: GameType.StormLeague, cost: 500)));
            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.UD | RewardType.Map, $"{map.Name} (UD)", map.Name, mode: GameType.UnrankedDraft, cost: 500)));
            rewards.AddRange(aramMaps.Select(map => new SupportedReward(RewardType.ARAM | RewardType.Map, $"{map.Name} (ARAM)", map.Name, mode: GameType.ARAM, cost: 500)));

            // rewards.Add(new SupportedReward(RewardType.QM | RewardType.Rank, $"Rank (QM)", mode: GameType.QuickMatch, cost: 750));
            // rewards.Add(new SupportedReward(RewardType.SL | RewardType.Rank, $"Rank (SL)", mode: GameType.StormLeague, cost: 750));
            // rewards.Add(new SupportedReward(RewardType.UD | RewardType.Rank, $"Rank (UD)", mode: GameType.UnrankedDraft, cost: 750));
            // rewards.Add(new SupportedReward(RewardType.ARAM | RewardType.Rank, $"Rank (ARAM)", mode: GameType.ARAM, cost: 750));

            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.QM | RewardType.Map | RewardType.Rank, $"{map.Name} (Rank QM)", map: map.Name, mode: GameType.QuickMatch, cost: 1000)));
            rewards.AddRange(rankedMaps.Select(map => new SupportedReward(RewardType.SL | RewardType.Map | RewardType.Rank, $"{map.Name} (Rank SL)", map: map.Name, mode: GameType.StormLeague, cost: 1000)));
            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.UD | RewardType.Map | RewardType.Rank, $"{map.Name} (Rank UD)", map.Name, mode: GameType.UnrankedDraft, cost: 1000)));
            rewards.AddRange(aramMaps.Select(map => new SupportedReward(RewardType.ARAM | RewardType.Map | RewardType.Rank, $"{map.Name} (Rank ARAM)", map: map.Name, mode: GameType.ARAM, cost: 1000)));

            rewards.Add(new SupportedReward(RewardType.ReplayId, "ReplayId", cost: 500));

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
