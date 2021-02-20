using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Obs
{
    public interface IObsController : IDisposable
    {
        void Connect();
        Task CycleReportAsync(int replayId);
        void SwapToGameScene();
        void SwapToWaitingScene();
        void UpdateMMRTier((int RankPoints, string Tier) mmr);
    }
}
