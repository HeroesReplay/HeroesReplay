using System;
using System.Threading;
using System.Threading.Tasks;

namespace HeroesReplay.Core.Obs;

public interface IObsController
{
    void BeginSession();
    void EndSession();
    void ConfigureFromContext();
    Task CycleReportAsync(CancellationToken cancellationToken = default);
    void SwapToGameScene();
    void UpdateReplayInfoVisibility(TimeSpan matchTime);
    void SwapToWaitingScene();
    ObsRecordingResult StartRecording();
    ObsRecordingResult StopRecording();
    ObsStreamResult StartStreaming();
    ObsStreamResult StopStreaming();

    /// <summary>
    /// The stream's health from GetStreamStatus (#395). Only <see cref="ObsStreamState.Live"/>
    /// is on air; an output that is active but reconnecting or frozen is not.
    /// </summary>
    ObsStreamHealth ReadStreamHealth();
    ObsRuntimeSnapshot ReadObsState();
}
