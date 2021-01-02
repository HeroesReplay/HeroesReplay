using HeroesReplay.Core.Models;
using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public sealed class StubController : IGameController
    {
        private readonly ILogger<StubController> logger;
        private readonly ISessionHolder sessionHolder;

        private TimeSpan TimeSpan { get; set; } = TimeSpan.Zero;

        public StubController(ILogger<StubController> logger, ISessionHolder sessionHolder)
        {
            this.logger = logger;
            this.sessionHolder = sessionHolder;
        }

        public void KillGame() { }

        public Task<StormReplay> LaunchAsync(StormReplay stormReplay) => Task.FromResult(stormReplay);

        public void SendFocus(int player) => logger.LogInformation($"Selected player {player}");

        public void SendPanel(Panel panel) => logger.LogInformation($"Selected panel {panel}");

        public Task<TimeSpan?> TryGetTimerAsync()
        {
            // return Task.FromResult(new TimeSpan?(sessionHolder.SessionData.End));

            var next = TimeSpan;
            TimeSpan = TimeSpan.Add(TimeSpan.FromSeconds(1));
            return Task.FromResult(new TimeSpan?(next));
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