using System;
using System.Collections.Generic;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;

namespace HeroesReplay.Core.Services.HeroesProfile;

public static class HeroesProfileRankEnricher
{
    private static readonly HashSet<string> FixedLadderRanks = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bronze",
        "Silver",
        "Gold",
        "Platinum",
        "Diamond",
        "Master",
        "Grandmaster",
    };

    public static bool IsStormLeague(string gameType)
    {
        if (string.IsNullOrWhiteSpace(gameType))
        {
            return false;
        }

        return string.Equals(gameType, "sl", StringComparison.OrdinalIgnoreCase)
            || string.Equals(gameType, "Storm League", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFixedLadderRank(string rank)
    {
        if (string.IsNullOrWhiteSpace(rank))
        {
            return false;
        }

        return FixedLadderRanks.Contains(rank.Trim());
    }

    public static bool ShouldLookup(HeroesProfileReplay replay)
    {
        if (replay == null || !IsStormLeague(replay.GameType))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(replay.Rank) || IsFixedLadderRank(replay.Rank);
    }

    /// <summary>
    /// Rank to store. A failed lookup returns null so the badge hides instead of the old ladder.
    /// </summary>
    public static string Resolve(string gameType, string currentRank, string heroesProfileTier)
    {
        if (!IsStormLeague(gameType))
        {
            return IsFixedLadderRank(currentRank) ? null : BlankToNull(currentRank);
        }

        string tier = BlankToNull(heroesProfileTier);
        if (tier != null && RankImage.SourceName(tier) != null)
        {
            return tier;
        }

        if (!string.IsNullOrWhiteSpace(currentRank) && !IsFixedLadderRank(currentRank))
        {
            return currentRank.Trim();
        }

        return null;
    }

    public static int? RoundMmr(double? mmr)
    {
        if (!mmr.HasValue)
        {
            return null;
        }

        return (int)Math.Round(mmr.Value, MidpointRounding.AwayFromZero);
    }

    private static string BlankToNull(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
