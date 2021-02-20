using HeroesReplay.Core.Models;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public sealed class StubController : IGameController
    {
        private readonly ILogger<StubController> logger;
        private readonly ISessionHolder sessionHolder;

        private List<TimeSpan?> Timers { get; set; } = new();

        public StubController(ILogger<StubController> logger, ISessionHolder sessionHolder)
        {
            this.logger = logger;
            this.sessionHolder = sessionHolder;
        }

        public void KillGame() { }


        public async Task<StormReplay> LaunchAsync(StormReplay stormReplay)
        {
            var timeSpans = Enumerable.Range((int)sessionHolder.SessionData.GatesOpen.TotalSeconds, (int)stormReplay.Replay.ReplayLength.TotalSeconds).ToList();
            var total = timeSpans.Count;
            var sections = (total / 16);

            for (int i = 0; i < 15; i++)
            {
                Timers.Add(TimeSpan.FromSeconds(timeSpans[sections * i]));
            }

            return await Task.FromResult(stormReplay).ConfigureAwait(false);
        }

        public void SendFocus(int player) => logger.LogInformation($"Selected player {player}");

        public void SendPanel(Panel panel) => logger.LogInformation($"Selected panel {panel}");

        public Task<TimeSpan?> TryGetTimerAsync()
        {
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