using Heroes.ReplayParser;

namespace HeroesReplay.Core.Models;

public static class PlayerPriorityRequest
{
    public static bool TryRead(string message, out int replayId, out int? playerIndex)
    {
        replayId = 0;
        playerIndex = null;
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

        if (!TrySlot(parts[1], out int index))
        {
            return false;
        }

        playerIndex = index;
        return true;
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

    public static string Digit(int playerIndex)
    {
        if (playerIndex == 9)
        {
            return "0";
        }

        if (playerIndex is >= 0 and <= 8)
        {
            return (playerIndex + 1).ToString();
        }

        return null;
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
