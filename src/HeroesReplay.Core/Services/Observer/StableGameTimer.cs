using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Services.Observer;

public sealed class StableGameTimer : IGameTimer
{
    private readonly AppSettings settings;
    private readonly IGameController controller;
    private readonly StableMatchClock clock = new();
    private readonly MatchTimerFilter filter;

    public StableGameTimer(
        AppSettings settings,
        IGameController controller,
        MatchTimerFilter filter
    )
    {
        this.settings = settings;
        this.controller = controller;
        this.filter = filter;
    }

    public void Reset() { }

    public Task<GameTimerReading> ReadAsync(CancellationToken cancellationToken)
    {
        if (!settings.Spectate.StableMatchClockEnabled)
        {
            return Task.FromResult(new GameTimerReading(false, "memory", "disabled", null));
        }

        if (controller.GetGameProcess() is not Process process)
        {
            return Task.FromResult(new GameTimerReading(false, "memory", "no-process", null));
        }

        StableClockSample sample = clock.Read(process);
        if (!sample.Ok)
        {
            return Task.FromResult(
                new GameTimerReading(
                    false,
                    "memory",
                    sample.Reason,
                    null,
                    sample.Ticks,
                    sample.Scale
                )
            );
        }

        TimeSpan time = TimeSpan.FromSeconds(sample.Seconds);
        TimeSpan limit =
            settings.Spectate.MaxTimerJump > TimeSpan.Zero
                ? settings.Spectate.MaxTimerJump
                : TimeSpan.FromSeconds(8);
        if (!filter.IsPlausible(time, limit))
        {
            return Task.FromResult(
                new GameTimerReading(
                    false,
                    "memory",
                    "implausible-jump",
                    time,
                    sample.Ticks,
                    sample.Scale
                )
            );
        }

        return Task.FromResult(
            new GameTimerReading(true, "memory", "ok", time, sample.Ticks, sample.Scale)
        );
    }
}
