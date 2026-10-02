namespace HeroesReplay.Core.Spectating;

public enum ReplaySessionKind
{
    Played,
    Held,
    Unplayed,

    /// <summary>
    /// The client was already on the award screen. Report scenes run and the next replay loads.
    /// This replay was not spectated, so it stays available.
    /// </summary>
    AwardFinished,
}

/// <summary>
/// A queued replay is consumed only after a verified completion.
/// Every other outcome stays queued. A dialog hold waits before the retry.
/// </summary>
public static class ReplaySession
{
    public static ReplaySessionKind Classify(MatchOutcome outcome)
    {
        if (outcome == MatchOutcome.VerifiedCompleted)
        {
            return ReplaySessionKind.Played;
        }

        if (outcome == MatchOutcome.AwardScreen)
        {
            return ReplaySessionKind.AwardFinished;
        }

        if (
            outcome == MatchOutcome.VersionMismatch
            || outcome == MatchOutcome.RegionUnavailable
            || outcome == MatchOutcome.BuildNotInstalled
        )
        {
            return ReplaySessionKind.Held;
        }

        return ReplaySessionKind.Unplayed;
    }

    public static bool StaysQueued(ReplaySessionKind kind) =>
        kind != ReplaySessionKind.Played && kind != ReplaySessionKind.AwardFinished;
}
