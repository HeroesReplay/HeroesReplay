using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Replays.Context;

namespace HeroesReplay.Core.Spectating.Clock;

/// <summary>
/// Headless clock for Capture:Method None. Each read is one second further, from 0:00 to the
/// replay length, then the clock stops.
/// </summary>
public sealed class StubGameTimer : IGameTimer
{
    private readonly IReplayContext context;
    private int? next;

    public StubGameTimer(IReplayContext context)
    {
        this.context = context;
    }

    public void Reset() => next = 0;

    public Task<GameTimerReading> ReadAsync(CancellationToken cancellationToken)
    {
        TimeSpan length = context.Current?.LoadedReplay?.Replay?.ReplayLength ?? TimeSpan.Zero;
        int last = Math.Max(1, (int)length.TotalSeconds);
        if (next is not int second || second > last)
        {
            return Task.FromResult(new GameTimerReading(false, "stub", "ended", null));
        }

        next = second + 1;
        return Task.FromResult(
            new GameTimerReading(true, "stub", "ok", TimeSpan.FromSeconds(second))
        );
    }
}
