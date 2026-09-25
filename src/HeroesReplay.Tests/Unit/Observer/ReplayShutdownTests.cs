using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Observer;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayShutdownTests
{
    [Fact]
    public void HungWindow_SkipsScreenshotAndStillKills()
    {
        var game = new FakeGame { Hung = true };

        ReplayShutdown.CaptureEndThenKill(game, NullLogger.Instance);

        Assert.Equal(new[] { "kill" }, game.Calls);
    }

    [Fact]
    public void ResponsiveWindow_CapturesBeforeKill()
    {
        var game = new FakeGame();

        ReplayShutdown.CaptureEndThenKill(game, NullLogger.Instance);

        Assert.Equal(new[] { "screenshot", "kill" }, game.Calls);
    }

    [Fact]
    public void ScreenshotThrows_StillKills()
    {
        var game = new FakeGame { ThrowOnScreenshot = true };

        ReplayShutdown.CaptureEndThenKill(game, NullLogger.Instance);

        Assert.Equal(new[] { "screenshot", "kill" }, game.Calls);
    }

    private sealed class FakeGame : IGameController
    {
        public bool Hung { get; set; }

        public bool ThrowOnScreenshot { get; set; }

        public List<string> Calls { get; } = new();

        public Task LaunchAsync() => throw new NotSupportedException();

        public Task<TimeSpan?> TryGetTimerAsync() => throw new NotSupportedException();

        public Task<bool> TrySeeEndScreenAsync(bool nearCore) => throw new NotSupportedException();

        public void SendFocus(int player) => throw new NotSupportedException();

        public void SendPanel(Panel panel) => throw new NotSupportedException();

        public void SaveEndScreenshot()
        {
            Calls.Add("screenshot");
            if (ThrowOnScreenshot)
            {
                throw new InvalidOperationException("capture blocked");
            }
        }

        public bool IsGameHung() => Hung;

        public bool IsGameRunning() => !Hung;

        public Process GetGameProcess() => throw new NotSupportedException();

        public void Kill() => Calls.Add("kill");
    }
}
