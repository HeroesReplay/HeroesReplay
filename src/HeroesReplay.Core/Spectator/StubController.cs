using HeroesReplay.Core.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public sealed class StubController : IGameController
    {
        private readonly ILogger<StubController> logger;

        private TimeSpan TimeSpan { get; set; } = TimeSpan.Zero;

        public StubController(ILogger<StubController> logger)
        {
            this.logger = logger;
        }

        public void KillGame() { }

        public Task<StormReplay> LaunchAsync(StormReplay stormReplay) => Task.FromResult(stormReplay);

        public void SendFocus(int player) => logger.LogInformation($"Selected player {player}");

        public void SendPanel(int panel) => logger.LogInformation($"Selected panel {panel}");

        public Task<TimeSpan?> TryGetTimerAsync()
        {
            var next = TimeSpan;
            TimeSpan = TimeSpan.Add(TimeSpan.FromSeconds(1));
            return Task.FromResult(new TimeSpan?(next));
        }
    }
}