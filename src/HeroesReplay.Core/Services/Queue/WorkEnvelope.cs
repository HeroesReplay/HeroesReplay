namespace HeroesReplay.Core.Services.Queue;

public enum WorkState
{
    Accepted,
    Downloading,
    Ready,
    Leased,
    Launched,
    ClockSeen,
    VerifiedCompleted,
    Failed,
    Quarantined,
    Refunded,
    Fulfilled,
}

/// <summary>
/// Queue removal and spectated-ids are not success. Only a verified completion is.
/// </summary>
public static class WorkEnvelope
{
    public static bool TryTransition(WorkState from, WorkState to)
    {
        return (from, to) switch
        {
            (WorkState.Accepted, WorkState.Downloading) => true,
            (WorkState.Downloading, WorkState.Ready) => true,
            (WorkState.Downloading, WorkState.Failed) => true,
            (WorkState.Downloading, WorkState.Quarantined) => true,
            (WorkState.Ready, WorkState.Leased) => true,
            (WorkState.Leased, WorkState.Launched) => true,
            (WorkState.Leased, WorkState.Failed) => true,
            (WorkState.Launched, WorkState.ClockSeen) => true,
            (WorkState.Launched, WorkState.Failed) => true,
            (WorkState.ClockSeen, WorkState.VerifiedCompleted) => true,
            (WorkState.ClockSeen, WorkState.Failed) => true,
            (WorkState.VerifiedCompleted, WorkState.Fulfilled) => true,
            (WorkState.Failed, WorkState.Refunded) => true,
            (WorkState.Failed, WorkState.Quarantined) => true,
            _ => false,
        };
    }

    public static WorkState AfterUnreadable() => WorkState.Quarantined;

    public static bool CountsAsPlayed(WorkState state) =>
        state == WorkState.VerifiedCompleted || state == WorkState.Fulfilled;
}
