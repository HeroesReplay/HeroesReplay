using System.Threading.Tasks;

using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware
{
    public interface IObsController
    {
        void SetSession(ObsEntry obsEntry);
        Task CycleReportAsync();
        void SwapToGameScene();
        void SwapToWaitingScene();
        void StartRecording();
        void StopRecording();
    }
}
