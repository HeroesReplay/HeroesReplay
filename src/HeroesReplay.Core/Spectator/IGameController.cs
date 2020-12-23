using HeroesReplay.Core.Shared;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface IGameController
    {
        Task<StormReplay> LaunchAsync(StormReplay stormReplay);
        Task<TimeSpan?> TryGetTimerAsync();
        void SendFocus(int player);
        void SendPanel(Panel panel);
        void KillGame();
        void SendToggleMaximumZoom();
        void ToggleControls();
        void ToggleTimer();
        void CameraFollow();
        void ToggleChatWindow();
        void SendToggleMediumZoom();
        void ToggleUnitPanel();
    }
}