using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Context;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

public sealed class OcrGameTimer : IGameTimer
{
    private readonly ILogger<OcrGameTimer> logger;
    private readonly AppSettings settings;
    private readonly IGameController controller;
    private readonly IReplayContext context;
    private readonly MatchTimerFilter filter;

    public OcrGameTimer(
        ILogger<OcrGameTimer> logger,
        AppSettings settings,
        IGameController controller,
        IReplayContext context,
        MatchTimerFilter filter
    )
    {
        this.logger = logger;
        this.settings = settings;
        this.controller = controller;
        this.context = context;
        this.filter = filter;
    }

    public void Reset() { }

    public async Task<GameTimerReading> ReadAsync(CancellationToken cancellationToken)
    {
        TimeSpan? time = await ReadReplayTimeAsync(cancellationToken).ConfigureAwait(false);
        if (!time.HasValue)
        {
            return new GameTimerReading(false, "ocr", "no-read", null);
        }

        return new GameTimerReading(true, "ocr", "ok", time);
    }

    private async Task<TimeSpan?> ReadReplayTimeAsync(CancellationToken cancellationToken)
    {
        TimeSpan? first = await ReadOneAsync().ConfigureAwait(false);
        if (first == null)
        {
            return null;
        }

        TimeSpan maxJump =
            settings.Spectate.MaxTimerJump > TimeSpan.Zero
                ? settings.Spectate.MaxTimerJump
                : TimeSpan.FromSeconds(8);
        if (filter.IsPlausible(first.Value, maxJump))
        {
            return first;
        }

        logger.LogWarning(
            "OCR timer {Candidate} jumped from {Last}; confirming with extra reads.",
            first,
            filter.LastAccepted
        );

        int extra = Math.Clamp(settings.Spectate.OcrConfirmReads, 1, 5) - 1;
        var samples = new List<TimeSpan> { first.Value };
        for (int i = 0; i < extra; i++)
        {
            await Task.Delay(75, cancellationToken).ConfigureAwait(false);
            TimeSpan? next = await ReadOneAsync().ConfigureAwait(false);
            if (next.HasValue)
            {
                samples.Add(next.Value);
            }
        }

        samples.Sort();
        if (samples.Count >= 2 && samples[^1] - samples[0] <= maxJump)
        {
            TimeSpan agreed = samples[samples.Count / 2];
            logger.LogWarning(
                "OCR timer caught up from {Last} to {Timer} after {Count} agreeing reads.",
                filter.LastAccepted,
                agreed,
                samples.Count
            );
            return agreed;
        }

        return null;
    }

    private async Task<TimeSpan?> ReadOneAsync()
    {
        TimeSpan? ui = await controller.TryGetTimerAsync().ConfigureAwait(false);
        if (!ui.HasValue)
        {
            return null;
        }

        TimeSpan candidate = ui.Value.Add(context.Current?.GatesOpen ?? TimeSpan.Zero);
        TimeSpan replayLength =
            context.Current?.LoadedReplay?.Replay?.ReplayLength ?? TimeSpan.Zero;
        if (replayLength > TimeSpan.Zero && candidate > replayLength + TimeSpan.FromMinutes(1))
        {
            logger.LogWarning(
                "Ignoring implausible timer {Timer} (replay length {Length}).",
                candidate,
                replayLength
            );
            return null;
        }

        return candidate;
    }
}
