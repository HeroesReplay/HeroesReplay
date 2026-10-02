using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Spectating.Clock;

public interface IGameTimer
{
    Task<GameTimerReading> ReadAsync(CancellationToken cancellationToken);

    void Reset();
}

public readonly record struct GameTimerReading(
    bool Ok,
    string Source,
    string Reason,
    TimeSpan? Time,
    int Ticks = 0,
    float Scale = 0,
    string Telemetry = null
);
