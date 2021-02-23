using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Obs
{
    public interface IObsController
    {
        Task CycleReportAsync();
        void SwapToGameScene();
        void SwapToWaitingScene();
        void UpdateMMRTier();
    }
}
