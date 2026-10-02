using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Spectating.Session;

namespace HeroesReplay.Core.Twitch.Rewards;

public enum RewardTerminal
{
    Verified,
    InvalidInput,
    UnavailableReplay,
    Duplicate,
    QueueFailure,
    LaunchFailure,
    SpectateFailure,
}

/// <summary>
/// Twitch status words for a finished redemption. This does not call Helix.
/// </summary>
public static class RewardRedemptionStatus
{
    public const string Fulfilled = "FULFILLED";
    public const string Canceled = "CANCELED";

    public static string Decide(RewardTerminal terminal)
    {
        switch (terminal)
        {
            case RewardTerminal.Verified:
                return Fulfilled;
            case RewardTerminal.InvalidInput:
                return Canceled;
            case RewardTerminal.UnavailableReplay:
                return Canceled;
            case RewardTerminal.Duplicate:
                return Canceled;
            case RewardTerminal.QueueFailure:
                return Canceled;
            case RewardTerminal.LaunchFailure:
                return Canceled;
            case RewardTerminal.SpectateFailure:
                return Canceled;
            default:
                return Canceled;
        }
    }

    public static RewardTerminal FromOutcome(MatchOutcome outcome)
    {
        if (outcome == MatchOutcome.VerifiedCompleted)
        {
            return RewardTerminal.Verified;
        }

        if (outcome == MatchOutcome.BuildNotInstalled || outcome == MatchOutcome.VersionMismatch)
        {
            return RewardTerminal.UnavailableReplay;
        }

        if (outcome == MatchOutcome.LoadTimedOut || outcome == MatchOutcome.ClientCrashed)
        {
            return RewardTerminal.LaunchFailure;
        }

        return RewardTerminal.SpectateFailure;
    }

    public static string ForQueue(RewardResponse response)
    {
        if (response == null || !response.Success)
        {
            if (response != null && response.Duplicate)
            {
                return Decide(RewardTerminal.Duplicate);
            }

            return Decide(RewardTerminal.QueueFailure);
        }

        if (response.Duplicate)
        {
            return Decide(RewardTerminal.Duplicate);
        }

        return null;
    }
}
