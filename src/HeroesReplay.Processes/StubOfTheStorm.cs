using System;
using System.Threading;
using System.Threading.Tasks;
using HeroesReplay.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Processes
{
    public class StubOfTheStorm : HeroesOfTheStorm
    {
        private TimeSpan timer = TimeSpan.Zero; 

        public StubOfTheStorm(CancellationTokenProvider tokenProvider, ScreenCapture screenCapture, ILogger<HeroesOfTheStorm> logger, IConfiguration configuration) : base(tokenProvider, screenCapture, logger, configuration)
        {

        }

        public override bool IsRunning => true;

        protected override Task<bool> GetWindowContainsAnyAsync(params string[] lines) => Task.FromResult(true);

        public override Task<TimeSpan?> TryGetTimerAsync()
        {
            timer = timer.Add(TimeSpan.FromSeconds(1));
            return Task.FromResult(new TimeSpan?(timer.AddNegativeOffset()));
        }

        public override Task<bool> WaitForSelectedReplayAsync(StormReplay stormReplay, CancellationToken token = default)
        {
            timer = TimeSpan.Zero;

            return Task.FromResult(true);
        }

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
        public override void SendTogglePause()
        {
           
        }
    }
}