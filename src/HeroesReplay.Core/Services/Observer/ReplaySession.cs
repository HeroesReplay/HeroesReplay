namespace HeroesReplay.Core.Services.Observer;

public enum ReplaySessionKind
{
    Played,
    Held,
    Unplayed,
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

        if (outcome == MatchOutcome.VersionMismatch || outcome == MatchOutcome.RegionUnavailable)
        {
            return ReplaySessionKind.Held;
        }

        return ReplaySessionKind.Unplayed;
    }

    public static bool StaysQueued(ReplaySessionKind kind) => kind != ReplaySessionKind.Played;
}
