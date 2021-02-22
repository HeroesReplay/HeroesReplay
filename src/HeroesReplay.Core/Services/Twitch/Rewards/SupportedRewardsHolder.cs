using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using System;
using System.Collections.Generic;
using System.Linq;

using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch
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
            List<Tier> tiers = Enum.GetValues(typeof(Tier)).OfType<Tier>().ToList();

            var rewards = new List<SupportedReward>();

            var rankedMaps = gameData.Maps.Where(m => m.Playable && m.RankedRotation);
            var unrankedMaps = gameData.Maps.Where(m => m.Playable && !m.RankedRotation && m.Type.Equals("standard"));
            var aramMaps = gameData.Maps.Where(m => m.Playable && m.Type.Equals("ARAM"));

            // 500
            rewards.Add(new SupportedReward(RewardType.QM, "Random (QM)", mode: GameMode.QuickMatch));
            rewards.Add(new SupportedReward(RewardType.SL, "Random (SL)", mode: GameMode.StormLeague));
            rewards.Add(new SupportedReward(RewardType.UD, "Random (UD)", mode: GameMode.Unranked));
            rewards.Add(new SupportedReward(RewardType.ARAM, "Random (ARAM)", mode: GameMode.ARAM));

            // 1000
            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.QMMap, $"{map.Name} (QM)", map.Name, mode: GameMode.QuickMatch)));
            rewards.AddRange(unrankedMaps.Select(map => new SupportedReward(RewardType.UDMap, $"{map.Name} (UD)", map.Name, mode: GameMode.Unranked)));
            rewards.AddRange(aramMaps.Select(map => new SupportedReward(RewardType.ARAMMap, $"{map.Name} (ARAM)", map.Name, mode: GameMode.ARAM)));
            rewards.AddRange(rankedMaps.Select(map => new SupportedReward(RewardType.SLMap, $"{map.Name} (SL)", map: map.Name, mode: GameMode.StormLeague)));

            // 1500
            rewards.AddRange(tiers.Select(tier => new SupportedReward(RewardType.QMTier, $"{tier} (QM)", tier: tier, mode: GameMode.QuickMatch)));
            rewards.AddRange(tiers.Select(tier => new SupportedReward(RewardType.SLTier, $"{tier} (SL)", tier: tier, mode: GameMode.StormLeague)));
            rewards.AddRange(tiers.Select(tier => new SupportedReward(RewardType.UDTier, $"{tier} (UD)", tier: tier, mode: GameMode.Unranked)));
            rewards.AddRange(tiers.Select(tier => new SupportedReward(RewardType.ARAMTier, $"{tier} (ARAM)", tier: tier, mode: GameMode.ARAM)));

            // 2000
            rewards.AddRange(unrankedMaps.SelectMany(map => tiers.Select(tier => new SupportedReward(RewardType.QMMapTier, $"{map.Name} (QM {tier})", map: map.Name, tier: tier, mode: GameMode.QuickMatch))));
            rewards.AddRange(rankedMaps.SelectMany(map => tiers.Select(tier => new SupportedReward(RewardType.SLMapTier, $"{map.Name} (SL {tier})", map: map.Name, tier: tier, mode: GameMode.StormLeague))));
            rewards.AddRange(unrankedMaps.SelectMany(map => tiers.Select(tier => new SupportedReward(RewardType.UDMap, $"{map.Name} (UD)", map.Name, tier: tier, mode: GameMode.Unranked))));
            rewards.AddRange(aramMaps.SelectMany(map => tiers.Select(tier => new SupportedReward(RewardType.ARAMMapTier, $"{map.Name} (ARAM {tier})", map: map.Name, tier: tier, mode: GameMode.ARAM))));

            // 10000
            rewards.Add(new SupportedReward(RewardType.SkipCurrent, "Skip Current"));

            // 500
            rewards.Add(new SupportedReward(RewardType.ReplayId, "ReplayId"));

            return rewards;
        }

        public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward)
        {
            reward = null;

            foreach (var item in Rewards)
            {
                if (item.RewardTitle == args.RewardTitle)
                {
                    reward = item;
                    return true;
                }
            }

            return false;
        }
    }
}
