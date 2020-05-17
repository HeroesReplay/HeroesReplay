using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Core.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Processes
{
    public class StubOfTheStorm : HeroesOfTheStorm
    {
        private readonly TimeSpan timer;
        private readonly ReplayHelper replayHelper;

        public StubOfTheStorm(
            ILogger<HeroesOfTheStorm> logger,
            IOptions<Settings> settings,
            CancellationTokenProvider tokenProvider,
            CaptureStrategy captureStrategy,
            ReplayHelper replayHelper) : base(logger, settings, tokenProvider, captureStrategy)
        {
            this.replayHelper = replayHelper;
            this.timer = TimeSpan.Zero;
        }

        public override bool IsRunning => true;
        protected override Task<bool> GetWindowContainsAnyAsync(IEnumerable<string> lines) => Task.FromResult(true);
        public override Task<TimeSpan?> TryGetTimerAsync() => Task.FromResult(new TimeSpan?(replayHelper.AddNegativeOffset(timer.Add(TimeSpan.FromSeconds(1)))));
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