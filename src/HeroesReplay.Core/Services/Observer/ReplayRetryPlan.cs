using System;

namespace HeroesReplay.Core.Services.Observer;

public enum ReplayRetryAction
{
    Consume,
    Front,
    Defer,
}

/// <summary>
/// A replay that never becomes a verified match must not sit at the front.
/// The first miss leaves the front so the next replay starts, and this one is
/// eligible again only after <see cref="DeferFor"/>.
/// A region-unavailable dialog is the exception: that replay stays at the front.
/// </summary>
public static class ReplayRetryPlan
{
    public const int MaxFrontAttempts = 1;

    public static readonly TimeSpan DeferFor = TimeSpan.FromMinutes(30);

    public static ReplayRetryAction Decide(MatchOutcome outcome, int attempt)
    {
        if (outcome == MatchOutcome.VerifiedCompleted)
        {
            return ReplayRetryAction.Consume;
        }

        if (outcome == MatchOutcome.BuildNotInstalled)
        {
            return ReplayRetryAction.Defer;
        }

        // The region dialog is the same client. Keep this replay and leave Heroes open.
        if (outcome == MatchOutcome.RegionUnavailable)
        {
            return ReplayRetryAction.Front;
        }

        if (attempt >= MaxFrontAttempts)
        {
            return ReplayRetryAction.Defer;
        }

        return ReplayRetryAction.Front;
    }

    /// <summary>
    /// A missing build did not start a client. A launch that is still downloading
    /// is that client doing its job. A region-unavailable dialog stays up so the
    /// same client can be tried again. A version-mismatch dialog is closed.
    /// </summary>
    public static bool ClosesClientAfterDefer(MatchOutcome outcome)
    {
        if (outcome == MatchOutcome.RegionUnavailable)
        {
            return false;
        }

        return outcome != MatchOutcome.BuildNotInstalled
            && outcome != MatchOutcome.LoadTimedOut
            && outcome != MatchOutcome.None
            && outcome != MatchOutcome.VerifiedCompleted;
    }
}
