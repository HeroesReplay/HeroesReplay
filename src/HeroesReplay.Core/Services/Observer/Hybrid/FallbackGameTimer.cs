using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Memory clock first. Screenshot OCR only when that read is not usable.
/// </summary>
public sealed class FallbackGameTimer : IGameTimer
{
    private readonly StableGameTimer memory;
    private readonly OcrGameTimer ocr;
    private readonly MatchTimerFilter filter;

    public FallbackGameTimer(StableGameTimer memory, OcrGameTimer ocr, MatchTimerFilter filter)
    {
        this.memory = memory;
        this.ocr = ocr;
        this.filter = filter;
    }

    public void Reset() => filter.Reset();

    public async Task<GameTimerReading> ReadAsync(CancellationToken cancellationToken)
    {
        GameTimerReading memoryReading = await memory
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        bool memoryUsable = IsPlayable(memoryReading);
        GameTimerReading screenshot = memoryUsable
            ? default
            : await ocr.ReadAsync(cancellationToken).ConfigureAwait(false);
        GameTimerReading chosen = Select(memoryReading, screenshot);
        if (chosen.Ok && chosen.Time.HasValue)
        {
            filter.Accept(chosen.Time.Value);
        }

        return chosen;
    }

    public static GameTimerReading Select(GameTimerReading memory, GameTimerReading screenshots)
    {
        if (IsPlayable(memory))
        {
            return memory;
        }

        if (screenshots.Ok && IsPlayable(screenshots))
        {
            return screenshots with { Reason = memory.Ok ? "memory-unplayable" : memory.Reason };
        }

        return screenshots with
        {
            Reason = string.IsNullOrWhiteSpace(memory.Reason) ? screenshots.Reason : memory.Reason,
        };
    }

    public static bool IsPlayable(GameTimerReading reading)
    {
        if (!reading.Ok || reading.Time == null)
        {
            return false;
        }

        TimeSpan time = reading.Time.Value;
        return time >= TimeSpan.FromMinutes(-3) && time <= TimeSpan.FromMinutes(90);
    }
}
