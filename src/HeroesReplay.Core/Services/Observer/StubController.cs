

namespace HeroesReplay.Core.Services.Observer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using HeroesReplay.Core.Models;
    using HeroesReplay.Core.Services.Context;

    using Microsoft.Extensions.Logging;

    public sealed class StubController : IGameController
    {
        private readonly ILogger<StubController> logger;
        private readonly IReplayContext context;

        private List<TimeSpan?> Timers { get; set; } = new();

        public StubController(ILogger<StubController> logger, IReplayContext context)
        {
            this.logger = logger;
            this.context = context;
        }

        public void Kill() { }

        public Task LaunchAsync()
        {
            var timeSpans = Enumerable.Range((int)context.Current.GatesOpen.TotalSeconds, (int)context.Current.LoadedReplay.Replay.ReplayLength.TotalSeconds).ToList();
            var total = timeSpans.Count;
            var sections = (total / 16);

            for (int i = 0; i < 15; i++)
            {
                Timers.Add(TimeSpan.FromSeconds(timeSpans[sections * i]));
            }

            return Task.CompletedTask;
        }

        public void SendFocus(int player) => logger.LogInformation($"Selected player {player}");

        public void SendPanel(Panel panel) => logger.LogInformation($"Selected panel {panel}");

        public Task<TimeSpan?> TryGetTimerAsync()
        {
            // return Task.FromResult(new TimeSpan?(TimeSpan.Zero));

            TimeSpan? timer = null;

            if (Timers.Count > 0)
            {
                timer = Timers[0];
                Timers.Remove(timer);
            }

            return Task.FromResult(timer);
        }

        public void SendToggleMaximumZoom() => logger.LogInformation($"SendToggleMaximumZoom");

        public void ToggleControls() => logger.LogInformation($"ToggleControls");

        public void ToggleTimer() => logger.LogInformation($"ToggleTimer");

        public void CameraFollow() => logger.LogInformation($"CameraFollow");

        public void ToggleChatWindow() => logger.LogInformation($"ToggleChatWindow");

        public void SendToggleMediumZoom() => logger.LogInformation($"SendToggleMediumZoom");

        public void ToggleUnitPanel() => logger.LogInformation($"SendToggleMediumZoom");
    }
}