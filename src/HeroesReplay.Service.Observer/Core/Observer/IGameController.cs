using System;
using System.Threading.Tasks;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Service.Spectator.Core.Observer
{
    public interface IGameController
    {
        Task LaunchAsync();
        Task<TimeSpan?> TryGetTimerAsync();
        void SendFocus(int player);
        void SendPanel(int panel);
        void Kill();
        void SendToggleMaximumZoom();
        void CameraFollow();
        void SendToggleMediumZoom();
    }
}