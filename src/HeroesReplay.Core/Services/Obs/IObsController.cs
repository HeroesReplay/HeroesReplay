using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Obs
{
    public interface IObsController
    {
        void Configure();
        Task CycleReportAsync(int replayId);
        void SwapToGameScene();
        void SwapToWaitingScene();
        void UpdateMMRTier((int RankPoints, string Tier) mmr);
    }
}
