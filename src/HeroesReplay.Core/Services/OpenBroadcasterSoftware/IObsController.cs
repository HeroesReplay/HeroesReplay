using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware
{
    public interface IObsController
    {
        void ConfigureFromContext();
        Task CycleReportAsync();
        void SwapToGameScene();
        void SwapToWaitingScene();
        void StartRecording();
        void StopRecording();
    }
}
