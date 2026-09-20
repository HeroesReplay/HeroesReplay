using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Context;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Services.Observer;

public sealed class StubController : IGameController
{
    private readonly ILogger<StubController> logger;
    private readonly IReplayContext context;
    private readonly Queue<TimeSpan?> timers = new();
    private Stopwatch replayOpened;

    public StubController(ILogger<StubController> logger, IReplayContext context)
    {
        this.logger = logger;
        this.context = context;
    }

    public TimeSpan? ReplayOpenElapsed =>
        replayOpened != null && replayOpened.IsRunning
            ? TimeSpan.FromSeconds(Math.Floor(replayOpened.Elapsed.TotalSeconds))
            : null;

    public void Kill() { }

    public Task LaunchAsync()
    {
        int duration = Math.Max(
            1,
            (int)context.Current.LoadedReplay.Replay.ReplayLength.TotalSeconds
        );
        timers.Clear();
        for (int second = 0; second <= duration; second++)
        {
            timers.Enqueue(TimeSpan.FromSeconds(second));
        }

        replayOpened = Stopwatch.StartNew();
        return Task.CompletedTask;
    }

    public void SendFocus(int player) => logger.LogInformation("Selected player {Player}", player);

    public void SendPanel(Panel panel) => logger.LogInformation("Selected panel {Panel}", panel);

    public void ZoomOut() => logger.LogInformation("Zoom out (Ctrl+Z)");

    public Task<TimeSpan?> TryGetTimerAsync()
    {
        if (timers.Count == 0)
        {
            return Task.FromResult<TimeSpan?>(null);
        }

        return Task.FromResult(timers.Dequeue());
    }
}
