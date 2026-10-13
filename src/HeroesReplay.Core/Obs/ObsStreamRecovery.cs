using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Obs;

/// <summary>What the stream reconcile does with the health it read (#395).</summary>
internal enum ObsStreamStep
{
    /// <summary>The stream is live: nothing to do.</summary>
    Keep,

    /// <summary>OBS did not report the stream: nothing is started or stopped.</summary>
    NotRead,

    /// <summary>OBS's own reconnect still has time (under <c>OBS:StreamStuckAfter</c>).</summary>
    WaitForObs,

    /// <summary>The last attempt failed and the next one is not due yet.</summary>
    Backoff,

    /// <summary>Stuck past <c>OBS:StreamStuckAfter</c>: StopStream, confirm inactive, then start.</summary>
    Restart,

    /// <summary>The output is inactive: the normal start.</summary>
    Start,
}

/// <summary>
/// The stream reconcile's memory between ticks (#395): when the next start or restart is due
/// after a failed one. A failed attempt waits <see cref="RetryDelays"/> (1, 2, then every 5
/// minutes) before the next; a live stream clears it.
/// </summary>
internal sealed class ObsStreamRecovery
{
    public static readonly TimeSpan DefaultStuckAfter = TimeSpan.FromSeconds(90);

    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
    ];

    private readonly object gate = new();
    private int failures;
    private DateTimeOffset? nextAttemptAt;

    public ObsStreamRecovery(TimeSpan stuckAfter)
    {
        StuckAfter = stuckAfter > TimeSpan.Zero ? stuckAfter : DefaultStuckAfter;
    }

    /// <summary><c>OBS:StreamStuckAfter</c>: how long OBS's own reconnect is left alone.</summary>
    public TimeSpan StuckAfter { get; }

    public DateTimeOffset? NextAttemptAt
    {
        get
        {
            lock (gate)
            {
                return nextAttemptAt;
            }
        }
    }

    public ObsStreamStep Decide(ObsStreamHealth health, DateTimeOffset now)
    {
        if (health == null || health.State == ObsStreamState.Unknown)
        {
            return ObsStreamStep.NotRead;
        }

        if (health.IsLive)
        {
            Recovered();
            return ObsStreamStep.Keep;
        }

        lock (gate)
        {
            if (nextAttemptAt is DateTimeOffset next && now < next)
            {
                return ObsStreamStep.Backoff;
            }
        }

        if (health.IsStuck)
        {
            return health.StuckFor(now) >= StuckAfter
                ? ObsStreamStep.Restart
                : ObsStreamStep.WaitForObs;
        }

        return ObsStreamStep.Start;
    }

    /// <summary>An attempt failed: returns how long until the next one.</summary>
    public TimeSpan Failed(DateTimeOffset now)
    {
        lock (gate)
        {
            TimeSpan delay = RetryDelays[Math.Min(failures, RetryDelays.Count - 1)];
            failures++;
            nextAttemptAt = now + delay;
            return delay;
        }
    }

    /// <summary>The stream is live: the next failure waits the first delay again.</summary>
    public void Recovered()
    {
        lock (gate)
        {
            failures = 0;
            nextAttemptAt = null;
        }
    }
}
