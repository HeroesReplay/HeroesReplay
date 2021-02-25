using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.Rewards
{
    public class SupportedRewardsHolder : ISupportedRewardsHolder
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
            List<GameRank> tiers = Enum.GetValues(typeof(GameRank)).OfType<GameRank>().ToList();

            var rewards = new List<SupportedReward>();

            var rankedMaps = gameData.Maps.Where(m => m.Playable && m.RankedRotation);
            var unrankedMaps = gameData.Maps.Where(m => m.Playable && !m.RankedRotation && m.Type.Equals("standard"));
            var aramMaps = gameData.Maps.Where(m => m.Playable && m.Type.Equals("ARAM"));

            // 500
            rewards.Add(new SupportedReward(RewardType.QM, "Random (QM)", mode: GameType.QuickMatch));
            rewards.Add(new SupportedReward(RewardType.SL, "Random (SL)", mode: GameType.StormLeague));
            rewards.Add(new SupportedReward(RewardType.UD, "Random (UD)", mode: GameType.UnrankedDraft));
            rewards.Add(new SupportedReward(RewardType.ARAM, "Random (ARAM)", mode: GameType.ARAM));

            // 1000
            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.QMMap, $"{map.Name} (QM)", map.Name, mode: GameType.QuickMatch)));
            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.UDMap, $"{map.Name} (UD)", map.Name, mode: GameType.UnrankedDraft)));
            rewards.AddRange(aramMaps.Select(map => new SupportedReward(RewardType.ARAMMap, $"{map.Name} (ARAM)", map.Name, mode: GameType.ARAM)));
            rewards.AddRange(rankedMaps.Select(map => new SupportedReward(RewardType.SLMap, $"{map.Name} (SL)", map: map.Name, mode: GameType.StormLeague)));

            // 1500
            rewards.AddRange(tiers.Select(tier => new SupportedReward(RewardType.QMTier, $"{tier} (QM)", tier: tier, mode: GameType.QuickMatch)));
            rewards.AddRange(tiers.Select(tier => new SupportedReward(RewardType.SLTier, $"{tier} (SL)", tier: tier, mode: GameType.StormLeague)));
            rewards.AddRange(tiers.Select(tier => new SupportedReward(RewardType.UDTier, $"{tier} (UD)", tier: tier, mode: GameType.UnrankedDraft)));
            rewards.AddRange(tiers.Select(tier => new SupportedReward(RewardType.ARAMTier, $"{tier} (ARAM)", tier: tier, mode: GameType.ARAM)));

            // 2000
            rewards.AddRange(unrankedMaps.SelectMany(map => tiers.Select(tier => new SupportedReward(RewardType.QMMapTier, $"{map.Name} (QM {tier})", map: map.Name, tier: tier, mode: GameType.QuickMatch))));
            rewards.AddRange(rankedMaps.SelectMany(map => tiers.Select(tier => new SupportedReward(RewardType.SLMapTier, $"{map.Name} (SL {tier})", map: map.Name, tier: tier, mode: GameType.StormLeague))));
            rewards.AddRange(unrankedMaps.SelectMany(map => tiers.Select(tier => new SupportedReward(RewardType.UDMap, $"{map.Name} (UD)", map.Name, tier: tier, mode: GameType.UnrankedDraft))));
            rewards.AddRange(aramMaps.SelectMany(map => tiers.Select(tier => new SupportedReward(RewardType.ARAMMapTier, $"{map.Name} (ARAM {tier})", map: map.Name, tier: tier, mode: GameType.ARAM))));

            // 500
            rewards.Add(new SupportedReward(RewardType.ReplayId, "ReplayId"));

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
