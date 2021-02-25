using HeroesReplay.Core.Models;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core
{
    public interface IGameController
    {
        Task LaunchAsync();
        Task<TimeSpan?> TryGetTimerAsync();
        void SendFocus(int player);
        void SendPanel(Panel panel);
        void Kill();
        void SendToggleMaximumZoom();
        void CameraFollow();
        void SendToggleMediumZoom();
    }
}