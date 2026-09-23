using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;
using TwitchLib.PubSub.Events;

namespace HeroesReplay.Core.Services.Twitch.Rewards;

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
        // Quick Match maps outside the ranked rotation. Not Unranked Draft.
        var unrankedMaps = gameData.Maps.Where(m =>
            m.Playable && !m.RankedRotation && m.Type.Equals("standard")
        );
        var aramMaps = gameData.Maps.Where(m => m.Playable && m.Type.Equals("ARAM"));

        rewards.Add(
            new SupportedReward(RewardType.QM, "Random (QM)", mode: GameType.QuickMatch, cost: 250)
        );
        rewards.Add(
            new SupportedReward(RewardType.SL, "Random (SL)", mode: GameType.StormLeague, cost: 250)
        );
        rewards.Add(
            new SupportedReward(RewardType.ARAM, "Random (ARAM)", mode: GameType.ARAM, cost: 250)
        );

        rewards.AddRange(
            unrankedMaps.Select(map => new SupportedReward(
                RewardType.QM | RewardType.Map,
                $"{map.Name} (QM)",
                map.Name,
                GameType.QuickMatch,
                500
            ))
        );
        rewards.AddRange(
            rankedMaps.Select(map => new SupportedReward(
                RewardType.SL | RewardType.Map,
                $"{map.Name} (SL)",
                map.Name,
                GameType.StormLeague,
                500
            ))
        );
        rewards.AddRange(
            aramMaps.Select(map => new SupportedReward(
                RewardType.ARAM | RewardType.Map,
                $"{map.Name} (ARAM)",
                map.Name,
                GameType.ARAM,
                500
            ))
        );

        // rewards.Add(new SupportedReward(RewardType.QM | RewardType.Rank, $"Rank (QM)", mode: GameType.QuickMatch, cost: 750));
        // rewards.Add(new SupportedReward(RewardType.SL | RewardType.Rank, $"Rank (SL)", mode: GameType.StormLeague, cost: 750));
        // rewards.Add(new SupportedReward(RewardType.ARAM | RewardType.Rank, $"Rank (ARAM)", mode: GameType.ARAM, cost: 750));

        rewards.AddRange(
            unrankedMaps.Select(map => new SupportedReward(
                RewardType.QM | RewardType.Map | RewardType.Rank,
                $"{map.Name} (Rank QM)",
                map.Name,
                GameType.QuickMatch,
                1000
            ))
        );
        rewards.AddRange(
            rankedMaps.Select(map => new SupportedReward(
                RewardType.SL | RewardType.Map | RewardType.Rank,
                $"{map.Name} (Rank SL)",
                map.Name,
                GameType.StormLeague,
                1000
            ))
        );
        rewards.AddRange(
            aramMaps.Select(map => new SupportedReward(
                RewardType.ARAM | RewardType.Map | RewardType.Rank,
                $"{map.Name} (Rank ARAM)",
                map.Name,
                GameType.ARAM,
                1000
            ))
        );

        rewards.Add(
            new SupportedReward(RewardType.ReplayId, nameof(RewardType.ReplayId), cost: 500)
        );
        rewards.Add(
            new SupportedReward(
                RewardType.ReplayId,
                "ReplayId + YouTube",
                cost: 1000,
                recordAndUpload: true
            )
        );

        return rewards;
    }

    public bool TryGetReward(OnRewardRedeemedArgs args, out SupportedReward reward)
    {
        reward = null;
        if (string.IsNullOrWhiteSpace(args?.RewardTitle))
        {
            return false;
        }

        string title = args.RewardTitle.Trim();
        foreach (SupportedReward item in Rewards)
        {
            if (item != null && string.Equals(item.Title, title, StringComparison.Ordinal))
            {
                reward = item;
                return true;
            }
        }

        // Ranked maps are not given QM rewards at sync time. Twitch allows 50
        // channel rewards, and those QM rewards are already on the channel.
        return TryMatchPlayableMap(title, out reward);
    }

    private bool TryMatchPlayableMap(string title, out SupportedReward reward)
    {
        reward = null;
        IReadOnlyList<Map> maps = gameData.Maps;
        if (maps == null)
        {
            return false;
        }

        foreach (Map map in maps)
        {
            if (map == null || !map.Playable || string.IsNullOrWhiteSpace(map.Name))
            {
                continue;
            }

            if (
                TryKnownMode(
                    title,
                    map,
                    " (Rank QM)",
                    RewardType.QM | RewardType.Map | RewardType.Rank,
                    GameType.QuickMatch,
                    1000,
                    "standard",
                    out reward
                )
                || TryKnownMode(
                    title,
                    map,
                    " (Rank SL)",
                    RewardType.SL | RewardType.Map | RewardType.Rank,
                    GameType.StormLeague,
                    1000,
                    "standard",
                    out reward
                )
                || TryKnownMode(
                    title,
                    map,
                    " (Rank ARAM)",
                    RewardType.ARAM | RewardType.Map | RewardType.Rank,
                    GameType.ARAM,
                    1000,
                    "ARAM",
                    out reward
                )
                || TryKnownMode(
                    title,
                    map,
                    " (QM)",
                    RewardType.QM | RewardType.Map,
                    GameType.QuickMatch,
                    500,
                    "standard",
                    out reward
                )
                || TryKnownMode(
                    title,
                    map,
                    " (SL)",
                    RewardType.SL | RewardType.Map,
                    GameType.StormLeague,
                    500,
                    "standard",
                    out reward
                )
                || TryKnownMode(
                    title,
                    map,
                    " (ARAM)",
                    RewardType.ARAM | RewardType.Map,
                    GameType.ARAM,
                    500,
                    "ARAM",
                    out reward
                )
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryKnownMode(
        string title,
        Map map,
        string suffix,
        RewardType rewardType,
        GameType mode,
        int cost,
        string mapType,
        out SupportedReward reward
    )
    {
        reward = null;
        if (map.Type == null || !map.Type.Equals(mapType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(title, map.Name + suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        reward = new SupportedReward(rewardType, map.Name + suffix, map.Name, mode, cost);
        return true;
    }
}
