using System;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Status;

namespace HeroesReplay.Core.Spectating;

public enum MatchOutcome
{
    None,
    VerifiedCompleted,
    ClientCrashed,
    ClientHung,
    VersionMismatch,
    RegionUnavailable,
    BuildNotInstalled,
    LoadTimedOut,
    Stopped,
    Canceled,
    AwardScreen,
}

/// <summary>
/// One verified session writes completion once. Any other outcome leaves those fields alone,
/// including a completion an earlier session already published.
/// </summary>
public sealed class MatchCompletion
{
    private bool written;

    public void Reset()
    {
        written = false;
    }

    public static MatchOutcome FromHold(ClientHoldReason hold)
    {
        if (hold == ClientHoldReason.VersionMismatch)
        {
            return MatchOutcome.VersionMismatch;
        }

        if (hold == ClientHoldReason.RegionUnavailable)
        {
            return MatchOutcome.RegionUnavailable;
        }

        if (hold == ClientHoldReason.BuildNotInstalled)
        {
            return MatchOutcome.BuildNotInstalled;
        }

        if (hold == ClientHoldReason.ClientNotReady)
        {
            return MatchOutcome.LoadTimedOut;
        }

        if (hold == ClientHoldReason.AwardScreen)
        {
            return MatchOutcome.AwardScreen;
        }

        return MatchOutcome.None;
    }

    public static MatchOutcome Normalize(
        MatchOutcome outcome,
        bool matchClockSeen,
        bool consoleStopped
    )
    {
        if (outcome == MatchOutcome.VerifiedCompleted && !matchClockSeen)
        {
            return consoleStopped ? MatchOutcome.Stopped : MatchOutcome.Canceled;
        }

        if (outcome != MatchOutcome.None)
        {
            return outcome;
        }

        return consoleStopped ? MatchOutcome.Stopped : MatchOutcome.Canceled;
    }

    public static bool AllowsMedia(MatchOutcome outcome, int hudSamples, TimeSpan recordedFor) =>
        outcome == MatchOutcome.VerifiedCompleted
        && MatchRecording.ShouldPublish(hudSamples, recordedFor);

    public void Apply(
        SpectatorStatus status,
        MatchOutcome outcome,
        int? replayId,
        int? winner,
        DateTimeOffset completedAt
    )
    {
        if (status == null || written)
        {
            return;
        }

        written = true;
        if (outcome != MatchOutcome.VerifiedCompleted)
        {
            return;
        }

        status.CompletedAt = completedAt;
        status.CompletedReplayId = replayId;
        status.CompletedWinnerTeam = winner;
    }
}
