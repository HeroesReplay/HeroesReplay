using System;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Twitch.Rewards;

/// <summary>
/// The sentence Twitch shows under a channel-point reward (its prompt, #423): what the reward
/// queues and, when it asks for input, what to enter. Every catalog reward has one, and the
/// sync writes it to the channel.
/// </summary>
public static class RewardDescription
{
    /// <summary>Twitch's limit for a custom reward's prompt.</summary>
    public const int MaxLength = 200;

    public const string ReplayIdInput =
        "Enter a recent-patch ReplayId, or ReplayId,BattleTag (12345678,Name#1234) to follow that player. Redeem before that match starts.";

    public static string For(
        RewardType rewardType,
        string map,
        GameType? mode,
        bool recordAndUpload
    )
    {
        if (rewardType == RewardType.ReplayId)
        {
            return recordAndUpload
                ? $"Queues the replay you choose. {ReplayIdInput} Recorded and uploaded to YouTube."
                : $"Queues the replay you choose. {ReplayIdInput}";
        }

        string modeName = ModeName(mode);
        if (rewardType.HasFlag(RewardType.Blind))
        {
            return $"Queues a random recent {modeName} replay with its rank hidden. Guess the rating, Bronze to Master, before it is revealed at the end of the match.";
        }

        if (string.IsNullOrWhiteSpace(map))
        {
            return $"Queues a random recent {modeName} replay to watch on stream.";
        }

        if (rewardType.HasFlag(RewardType.Rank))
        {
            return $"Queues a recent {modeName} replay on {map} at the rank you enter: {string.Join(", ", Enum.GetNames<GameRank>())} (no division).";
        }

        return $"Queues a random recent {modeName} replay on {map} to watch on stream.";
    }

    private static string ModeName(GameType? mode)
    {
        return mode switch
        {
            GameType.QuickMatch => "Quick Match",
            GameType.StormLeague => "Storm League",
            GameType.ARAM => "ARAM",
            GameType.UnrankedDraft => "Unranked Draft",
            _ => "Heroes of the Storm",
        };
    }
}
