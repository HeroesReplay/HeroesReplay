using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.HeroesProfile;

/// <summary>
/// One Heroes Profile key's request pace, shared by every caller in a process (#433): the
/// statistics calls and the player lookups count against the same 60 requests a minute, job polls
/// included. Each request waits until <see cref="Spacing"/> has passed since the last one, and a
/// request in a lane (<c>group_by_map</c>, once a minute) also waits for that lane's own gap.
/// </summary>
public sealed class HeroesProfilePacer
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> lanes = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private DateTimeOffset? last;

    public HeroesProfilePacer(
        TimeSpan spacing,
        Func<DateTimeOffset> clock = null,
        Func<TimeSpan, CancellationToken, Task> delay = null
    )
    {
        Spacing = spacing > TimeSpan.Zero ? spacing : TimeSpan.FromMilliseconds(1100);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        this.delay = delay ?? ((wait, token) => Task.Delay(wait, token));
    }

    /// <summary>The least time between two requests.</summary>
    public TimeSpan Spacing { get; }

    /// <summary>
    /// Waits until the next request keeps the pace, then counts it as sent. A
    /// <paramref name="lane"/> also keeps <paramref name="laneGap"/> since that lane's last request.
    /// </summary>
    public async Task WaitAsync(
        CancellationToken cancellationToken,
        string lane = null,
        TimeSpan laneGap = default
    )
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = clock();
            TimeSpan wait = TimeSpan.Zero;
            if (last is DateTimeOffset previous)
            {
                wait = Max(wait, previous + Spacing - now);
            }

            if (lane != null && lanes.TryGetValue(lane, out DateTimeOffset laneLast))
            {
                wait = Max(wait, laneLast + laneGap - now);
            }

            if (wait > TimeSpan.Zero)
            {
                await delay(wait, cancellationToken).ConfigureAwait(false);
            }

            last = clock();
            if (lane != null)
            {
                lanes[lane] = last.Value;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
