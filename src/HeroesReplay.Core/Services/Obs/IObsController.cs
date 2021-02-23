using HeroesReplay.Core.Services.HeroesProfile;

using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Obs
{
    public interface IObsController : IDisposable
    {
        void Configure();
        Task CycleReportAsync();
        void SwapToGameScene();
        void SwapToWaitingScene();
        void UpdateMMRTier();
    }
}
