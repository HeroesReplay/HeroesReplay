using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Observer
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