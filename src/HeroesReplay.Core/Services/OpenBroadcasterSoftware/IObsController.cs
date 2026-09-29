using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public interface IObsController
{
    void BeginSession();
    void EndSession();
    void ConfigureFromContext();
    Task CycleReportAsync();
    void SwapToGameScene();
    void UpdateReplayInfoVisibility(TimeSpan matchTime);
    void SwapToWaitingScene();
    ObsRecordingResult StartRecording();
    ObsRecordingResult StopRecording();
    ObsStreamResult StartStreaming();
    ObsStreamResult StopStreaming();
    bool IsStreaming();
}
