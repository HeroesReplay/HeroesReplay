using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer;

public interface IGameController
{
    Task LaunchAsync();
    Task StartAuthenticatedReplayAsync(string replayPath);
    Task<bool> OpenReplayFromHomeScreenAsync(string replayPath);
    Task<TimeSpan?> TryGetTimerAsync();
    Task<bool> IsReplayPresentedAsync(LoadedReplay replay);
    Task<bool> TrySeeEndScreenAsync(bool nearCore);
    void SendFocus(int player);
    void SendPanel(Panel panel);
    void ShowSelectedUnit();
    void SaveEndScreenshot();
    bool IsGameHung();
    bool IsGameRunning();
    Process GetGameProcess();
    void Kill();
}
