using HeroesReplay.Core.Requests;

namespace HeroesReplay.Core.Replays;

/// <summary>
/// The player the camera follows for a whole replay: a viewer request's BattleTag, else the
/// cycle's focus pick (#433). Null when neither names a player of this replay.
/// </summary>
public static class ReplayFocus
{
    public static int? PlayerIndex(LoadedReplay loaded)
    {
        RewardRequest request = loaded?.RewardQueueItem?.Request;
        if (request != null)
        {
            return PlayerPriorityRequest.PlayerIndex(loaded.Replay, request);
        }

        int players = loaded?.Replay?.Players?.Length ?? 0;
        return loaded?.FocusPick?.PlayerIndex is int index && index >= 0 && index < players
            ? index
            : null;
    }
}
