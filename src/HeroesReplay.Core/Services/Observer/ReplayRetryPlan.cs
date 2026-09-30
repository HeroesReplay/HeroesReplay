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

        if (attempt >= MaxFrontAttempts)
        {
            return ReplayRetryAction.Defer;
        }

        return ReplayRetryAction.Front;
    }
}
