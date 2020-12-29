using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.Obs
{
    public interface IObsController
    {
        Task CycleReportAsync(int replayId);
        Task SwapToGameSceneAsync();
        Task SwapToWaitingSceneAsync();
    }
}
