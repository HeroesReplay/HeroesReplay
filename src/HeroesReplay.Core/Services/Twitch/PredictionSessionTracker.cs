using System;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Twitch;

public enum PredictionSignalKind
{
    None,
    Open,
    Resolve,
    Cancel,
}

public readonly record struct PredictionSignal(
    PredictionSignalKind Kind,
    int ReplayId,
    string Map,
    int? WinnerTeam
);

/// <summary>
/// Decides when the Twitch process should open or settle a Blue/Red prediction from spectator
/// status. The spectator does not call Helix. Completion fields stay on the status file across
/// the next replay load so a slow poll still sees the result.
/// </summary>
public sealed class PredictionSessionTracker
{
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromMinutes(2);

    private int? openReplayId;
    private DateTimeOffset openedAt;
    private string openMap;

    public PredictionSignal Observe(SpectatorStatus status, DateTimeOffset utcNow)
    {
        if (status == null)
        {
            return default;
        }

        if (
            openReplayId.HasValue
            && status.CompletedReplayId == openReplayId
            && status.CompletedAt.HasValue
            && status.CompletedAt.Value > openedAt
        )
        {
            return Finish(status.CompletedWinnerTeam);
        }

        if (openReplayId.HasValue && SessionAbandoned(status, utcNow))
        {
            return Finish(null);
        }

        if (CanOpen(status))
        {
            openReplayId = status.ReplayId;
            openedAt = status.UpdatedAt;
            openMap = status.Map;
            return new PredictionSignal(
                PredictionSignalKind.Open,
                status.ReplayId.Value,
                status.Map,
                null
            );
        }

        return default;
    }

    private PredictionSignal Finish(int? winnerTeam)
    {
        int replayId = openReplayId.Value;
        string map = openMap;
        openReplayId = null;
        openMap = null;
        openedAt = default;
        PredictionSignalKind kind = winnerTeam.HasValue
            ? PredictionSignalKind.Resolve
            : PredictionSignalKind.Cancel;
        return new PredictionSignal(kind, replayId, map, winnerTeam);
    }

    private bool SessionAbandoned(SpectatorStatus status, DateTimeOffset utcNow)
    {
        if (utcNow - status.UpdatedAt > AbandonAfter)
        {
            return true;
        }

        bool movedOn =
            !status.SnapshotStale
            && status.SpectatorRunning
            && status.ReplayId.HasValue
            && status.ReplayId != openReplayId;
        if (movedOn)
        {
            return true;
        }

        return !status.SnapshotStale
            && !status.SpectatorRunning
            && status.Phase is "Idle" or "EndDetected";
    }

    private bool CanOpen(SpectatorStatus status)
    {
        return !status.SnapshotStale
            && status.SpectatorRunning
            && status.Phase == "TimerDetected"
            && status.ReplayId is int replayId
            && replayId != openReplayId
            && !string.IsNullOrWhiteSpace(status.Map);
    }
}
