using System;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Requests;

public static class PlayerPriorityRequest
{
    /// <summary>
    /// Reads "ReplayId" or "ReplayId,Name#1234". The BattleTag names the player to follow. It is
    /// matched to an observe slot once the replay is parsed (<see cref="PlayerIndex"/>).
    /// </summary>
    public static bool TryRead(string message, out int replayId, out string battleTag)
    {
        replayId = 0;
        battleTag = null;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string[] parts = message.Split(
            ',',
            System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries
        );
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out replayId) || replayId <= 0)
        {
            return false;
        }

        if (parts.Length == 1)
        {
            return true;
        }

        return TryBattleTag(parts[1], out battleTag);
    }

    /// <summary>A BattleTag is a name, '#', and its number: Kazpa#2345.</summary>
    public static bool TryBattleTag(string text, out string battleTag)
    {
        battleTag = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split('#');
        if (parts.Length != 2)
        {
            return false;
        }

        string name = parts[0];
        string number = parts[1];
        if (
            name.Length == 0
            || name.Any(char.IsWhiteSpace)
            || number.Length is 0 or > 8
            || !number.All(char.IsAsciiDigit)
        )
        {
            return false;
        }

        battleTag = $"{name}#{number}";
        return true;
    }

    /// <summary>
    /// The observe slot to follow. A Twitch request names a BattleTag, which is looked up among
    /// the replay's players. The CLI file option sets the slot directly. Null when neither is
    /// set, or when the BattleTag did not play in this replay.
    /// </summary>
    public static int? PlayerIndex(Replay replay, RewardRequest request)
    {
        if (request == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.BattleTag))
        {
            return request.PlayerIndex;
        }

        Player[] players = replay?.Players;
        if (players == null)
        {
            return null;
        }

        for (int index = 0; index < players.Length; index++)
        {
            Player player = players[index];
            if (
                !string.IsNullOrWhiteSpace(player?.Name)
                && string.Equals(
                    $"{player.Name}#{player.BattleTag}",
                    request.BattleTag,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return index;
            }
        }

        return null;
    }

    public static bool TrySlot(string text, out int playerIndex)
    {
        playerIndex = -1;
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length != 1)
        {
            return false;
        }

        char digit = text.Trim()[0];
        if (digit == '0')
        {
            playerIndex = 9;
            return true;
        }

        if (digit is < '1' or > '9')
        {
            return false;
        }

        playerIndex = digit - '1';
        return true;
    }

    public static bool BlocksBecauseMatchStarted(
        int? spectatingReplayId,
        string phase,
        int requestedReplayId
    )
    {
        if (spectatingReplayId != requestedReplayId)
        {
            return false;
        }

        return phase is "Loading" or "TimerDetected" or "EndDetected";
    }

    public static string HeroName(Replay replay, int? playerIndex)
    {
        if (replay?.Players == null || playerIndex is not int index)
        {
            return null;
        }

        if (index < 0 || index >= replay.Players.Length)
        {
            return null;
        }

        Player player = replay.Players[index];
        if (!string.IsNullOrWhiteSpace(player?.Character))
        {
            return player.Character;
        }

        return string.IsNullOrWhiteSpace(player?.Name) ? null : player.Name;
    }

    /// <summary>
    /// Coaching view: follow this hero whenever they are alive, including camps and rotations.
    /// While they are dead, the normal camera is used until they respawn.
    /// </summary>
    public static bool Watch(bool alive)
    {
        return alive;
    }
}
