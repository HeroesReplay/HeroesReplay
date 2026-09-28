using System;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

public interface IObsController
{
    void BeginSession();
    void EndSession();
    void ConfigureFromContext();
    Task CycleReportAsync(NextGameSignal nextGame);
    void SwapToGameScene();
    void UpdateReplayInfoVisibility(TimeSpan matchTime);
    void SwapToWaitingScene();
    void StartRecording();
    void StopRecording();
    void StartStreaming();
    void StopStreaming();
    bool IsStreaming();
}
