using System;
using HeroesReplay.Core.Spectating.Session;
using HeroesReplay.Core.Status;

namespace HeroesReplay.Core.Twitch.Predictions;

public enum PredictionSignalKind
{
    None,
    Open,
    Resolve,
    Cancel,
    Disabled,
}

public enum PredictionSessionFault
{
    None,
    Crash,
    Stop,
    VersionMismatch,
    LoginFailure,
    NoClock,
}

/// <param name="Winner">
/// The winning outcome's position: the team (0 Blue, 1 Red) for Blue/Red, the rating
/// (0 Bronze to 5 Master) for a rating prediction. Null for a cancel.
/// </param>
public readonly record struct PredictionSignal(
    PredictionSignalKind Kind,
    int ReplayId,
    string Map,
    int? Winner,
    int Attempt,
    PredictionKind Prediction = PredictionKind.Winner
);

/// <summary>
/// Decides when the Twitch process should open or settle a prediction from spectator status:
/// Blue/Red, or the rating guess for a Blind Rating replay (#411). The spectator does not call
/// Helix. Completion fields stay on the status file across the next replay load so a slow poll
/// still sees the result.
/// </summary>
public sealed class PredictionSessionTracker
{
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromMinutes(2);

    private int? openReplayId;
    private int openAttempt;
    private int? announcedDisabledReplayId;
    private DateTimeOffset openedAt;
    private string openMap;
    private PredictionKind openKind;

    public void Release(int replayId)
    {
        if (openReplayId == replayId)
        {
            openReplayId = null;
            openAttempt = 0;
            openMap = null;
            openedAt = default;
            openKind = PredictionKind.Winner;
        }
    }

    public void Restore(
        int replayId,
        int attempt,
        DateTimeOffset openedAt,
        string map,
        PredictionKind kind = PredictionKind.Winner
    )
    {
        if (replayId <= 0 || attempt <= 0)
        {
            return;
        }

        openReplayId = replayId;
        openAttempt = attempt;
        this.openedAt = openedAt;
        openMap = map;
        openKind = kind;
    }

    /// <summary>
    /// A verified played session resolves once. Crash, stop, version mismatch, login failure,
    /// and a session with no match clock cancel once. Callers that do not know the session pass
    /// this in; the spectator does not.
    /// </summary>
    public static PredictionSignalKind DecideSession(
        ReplaySessionKind kind,
        bool matchClockSeen,
        int? winnerTeam,
        PredictionSessionFault fault,
        bool alreadySettled
    )
    {
        if (alreadySettled)
        {
            return PredictionSignalKind.None;
        }

        bool verified =
            fault == PredictionSessionFault.None
            && kind == ReplaySessionKind.Played
            && matchClockSeen
            && winnerTeam.HasValue;
        return verified ? PredictionSignalKind.Resolve : PredictionSignalKind.Cancel;
    }

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
            return Finish(
                openKind == PredictionKind.Rating
                    ? RatingPrediction.OutcomeIndex(status.CompletedRating)
                    : status.CompletedWinnerTeam
            );
        }

        if (openReplayId.HasValue && SessionAbandoned(status, utcNow))
        {
            return Finish(null);
        }

        if (CanOpen(status))
        {
            if (status.SuppressPredictions)
            {
                if (announcedDisabledReplayId == status.ReplayId)
                {
                    return default;
                }

                announcedDisabledReplayId = status.ReplayId;
                return new PredictionSignal(
                    PredictionSignalKind.Disabled,
                    status.ReplayId.Value,
                    status.Map,
                    null,
                    0
                );
            }

            openReplayId = status.ReplayId;
            openAttempt = 0;
            openedAt = status.UpdatedAt;
            openMap = status.Map;
            openKind = status.RatingPrediction ? PredictionKind.Rating : PredictionKind.Winner;
            return new PredictionSignal(
                PredictionSignalKind.Open,
                status.ReplayId.Value,
                status.Map,
                null,
                openAttempt,
                openKind
            );
        }

        return default;
    }

    private PredictionSignal Finish(int? winner)
    {
        int replayId = openReplayId.Value;
        int attempt = openAttempt;
        string map = openMap;
        PredictionKind prediction = openKind;
        openReplayId = null;
        openAttempt = 0;
        openMap = null;
        openedAt = default;
        openKind = PredictionKind.Winner;
        PredictionSignalKind kind = winner.HasValue
            ? PredictionSignalKind.Resolve
            : PredictionSignalKind.Cancel;
        return new PredictionSignal(kind, replayId, map, winner, attempt, prediction);
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
