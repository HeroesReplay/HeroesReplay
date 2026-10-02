using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Replays;
using Microsoft.Extensions.Logging;

namespace HeroesReplay.Core.Spectating.Control;

/// <summary>
/// Headless controller for Capture:Method None. Its clock is <see cref="Clock.StubGameTimer"/>.
/// </summary>
public sealed class StubController : IGameController
{
    private readonly ILogger<StubController> logger;

    public StubController(ILogger<StubController> logger)
    {
        this.logger = logger;
    }

    public void Kill() { }

    public void SaveEndScreenshot() { }

    public bool IsGameHung() => false;

    public bool IsGameRunning() => true;

    public bool ReplayFileOpened => false;

    public Process GetGameProcess() => null;

    public Task<bool> StartAuthenticatedReplayAsync(
        string replayPath,
        string replayVersion = null
    ) => Task.FromResult(false);

    public Task<bool> OpenReplayFromHomeScreenAsync(string replayPath) => Task.FromResult(false);

    public Task<ClientHoldReason> LaunchAsync() => Task.FromResult(ClientHoldReason.None);

    public void SendFocus(int player) => logger.LogInformation("Selected player {Player}", player);

    public void SendPanel(Panel panel) => logger.LogInformation("Selected panel {Panel}", panel);

    public void ShowSelectedUnit() => logger.LogInformation("Show selected unit (Ctrl+Alt+K).");

    public Task<bool> IsReplayPresentedAsync(LoadedReplay replay) => Task.FromResult(false);

    public Task<TimeSpan?> TryReadRunningMatchClockAsync() => Task.FromResult<TimeSpan?>(null);

    public Task<bool> TrySeeEndScreenAsync(bool nearCore) => Task.FromResult(false);
}
