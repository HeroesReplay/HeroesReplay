namespace HeroesReplay.Core.Services.Observer;

public enum ReplaySessionKind
{
    Played,
    Held,
    Unplayed,
}

/// <summary>
/// A queued replay is consumed only after the match clock was seen.
/// A dialog hold and a session with no clock stay in the queue.
/// </summary>
public static class ReplaySession
{
    public static ReplaySessionKind Classify(ClientHoldReason hold, bool matchClockSeen)
    {
        if (hold != ClientHoldReason.None)
        {
            return ReplaySessionKind.Held;
        }

        return matchClockSeen ? ReplaySessionKind.Played : ReplaySessionKind.Unplayed;
    }

    public static bool StaysQueued(ReplaySessionKind kind) => kind != ReplaySessionKind.Played;
}
