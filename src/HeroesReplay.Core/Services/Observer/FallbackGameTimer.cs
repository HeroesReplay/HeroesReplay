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
        GameTimerReading screenshot = memoryReading.Ok
            ? default
            : await ocr.ReadAsync(cancellationToken).ConfigureAwait(false);
        GameTimerReading chosen = Select(memoryReading, screenshot);
        if (chosen.Ok)
        {
            filter.Accept(chosen.Time.Value);
        }

        return chosen;
    }

    public static GameTimerReading Select(GameTimerReading memory, GameTimerReading screenshots)
    {
        if (memory.Ok)
        {
            return memory;
        }

        return screenshots with
        {
            Reason = memory.Reason,
        };
    }
}
