using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer;

public interface IGameController
{
    Task LaunchAsync();
    Task<TimeSpan?> TryGetTimerAsync();
    Task<bool> TrySeeEndScreenAsync(bool nearCore);
    void SendFocus(int player);
    void SendPanel(Panel panel);
    void SaveEndScreenshot();
    bool IsGameHung();
    bool IsGameRunning();
    Process GetGameProcess();
    void Kill();
}
