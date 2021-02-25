using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Stream
{
    public interface IObsController
    {
        Task CycleReportAsync();
        void SwapToGameScene();
        void SwapToWaitingScene();
        void SetRankImage();
    }
}
