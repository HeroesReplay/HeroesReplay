using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using HeroesClientSDK;
using HeroesReplay.Core.Spectating.Control;

namespace HeroesReplay.Core.Spectating.Clock;

/// <summary>
/// The match clock. It is read from memory only; the HUD clock is never cropped or OCR'd.
/// A read that is not ok means the match has not started, is between matches, or is over.
/// </summary>
public sealed class StableGameTimer : IGameTimer
{
    private readonly IGameController controller;
    private readonly MatchClock clock = new();

    public StableGameTimer(IGameController controller)
    {
        this.controller = controller;
    }

    public void Reset() => clock.BeginMatch();

    public Task<GameTimerReading> ReadAsync(CancellationToken cancellationToken)
    {
        if (controller.GetGameProcess() is not Process process)
        {
            return Task.FromResult(new GameTimerReading(false, "memory", "no-process", null));
        }

        MatchClockSample sample = clock.Read(process);
        string telemetry = clock.LastTelemetry.State;
        if (!sample.Ok)
        {
            return Task.FromResult(
                new GameTimerReading(
                    false,
                    "memory",
                    sample.Reason,
                    null,
                    sample.Ticks,
                    sample.Scale,
                    telemetry
                )
            );
        }

        return Task.FromResult(
            new GameTimerReading(
                true,
                "memory",
                "ok",
                TimeSpan.FromSeconds(sample.Seconds),
                sample.Ticks,
                sample.Scale,
                telemetry
            )
        );
    }
}
