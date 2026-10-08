using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HeroesReplay.Core.Analysis;
using HeroesReplay.Core.GameClient;
using HeroesReplay.Core.Replays;

namespace HeroesReplay.Core.Spectating.Control;

public interface IGameController
{
    Task<ClientHoldReason> LaunchAsync();
    Task<bool> StartAuthenticatedReplayAsync(string replayPath, string replayVersion = null);
    Task<bool> OpenReplayFromHomeScreenAsync(string replayPath);
    Task<TimeSpan?> TryReadRunningMatchClockAsync();
    Task<bool> IsReplayPresentedAsync(LoadedReplay replay);
    Task<bool> TrySeeEndScreenAsync(bool nearCore);

    /// <summary>
    /// Shadow mode (#292): read the end screen next to memory from the core-death time on. It
    /// decides nothing.
    /// </summary>
    Task ShadowEndScreenAsync() => Task.CompletedTask;

    void SendFocus(int player);
    void SendPanel(Panel panel);
    void ShowSelectedUnit();
    void SaveEndScreenshot();
    bool IsGameHung();
    bool IsGameRunning();
    bool ReplayFileOpened { get; }
    Process GetGameProcess();
    void Kill();
}
