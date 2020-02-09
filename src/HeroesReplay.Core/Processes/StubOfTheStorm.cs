using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Processes
{
    public class StubOfTheStorm : HeroesOfTheStorm
    {
        private readonly TimeSpan timer;

        public StubOfTheStorm(CancellationTokenProvider tokenProvider, CaptureStrategy captureStrategy, ILogger<HeroesOfTheStorm> logger, IConfiguration configuration) : base(tokenProvider, captureStrategy, logger, configuration)
        {
            timer = TimeSpan.Zero;
        }

        public override bool IsRunning => true;
        protected override Task<bool> GetWindowContainsAnyAsync(params string[] lines) => Task.FromResult(true);
        public override Task<TimeSpan?> TryGetTimerAsync() => Task.FromResult(new TimeSpan?(timer.Add(TimeSpan.FromSeconds(1)).AddNegativeOffset()));
        public override Task<bool> LaunchSelectedReplayAsync(StormReplay stormReplay, CancellationToken token = default) => Task.FromResult(true);
        public override Task<bool> WaitForMapLoadingAsync(StormReplay stormReplay, CancellationToken token = default) => Task.FromResult(true);
        public override Task ConfigureClientAsync() => Task.CompletedTask;
        public override void SendPanelChange(int index) { }
        public override void SendFocusHero(int index) { }
        public override void SendFollow() { }
        public override void SendToggleZoom() { }
        public override void SendToggleChat() { }
        public override void SendToggleTime() { }
        public override void SendToggleControls() { }
        public override void SendToggleBottomConsole() { }
        public override void SendToggleInfoPanel() { }
        public override Task<bool> TryKillGameAsync() => Task.FromResult(true);
        public override void SendTogglePause() { }
    }
}