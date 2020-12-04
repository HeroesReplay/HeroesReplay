using HeroesReplay.Core.Shared;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public sealed class StubController : IGameController
    {
        private TimeSpan TimeSpan { get; set; } = TimeSpan.Zero;

        public void KillGame() { }

        public Task<StormReplay> LaunchAsync(StormReplay stormReplay) => Task.FromResult(stormReplay);

        public void SendFocus(int player) => Console.WriteLine($"Sending player {player}");

        public void SendPanel(int panel) => Console.WriteLine($"Sending panel {panel}");

        public Task<TimeSpan?> TryGetTimerAsync()
        {
            var next = TimeSpan;
            TimeSpan = TimeSpan.Add(TimeSpan.FromSeconds(1));
            return Task.FromResult(new TimeSpan?(next));
        }
    }
}