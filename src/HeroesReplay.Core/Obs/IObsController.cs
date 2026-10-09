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

    /// <summary>
    /// Like <see cref="ReadStreamHealth"/>, for a decision between replays (#396): when the
    /// replay's session already ended and OBS runs, the websocket identifies first, so a live
    /// stream is not read as <see cref="ObsStreamState.Unknown"/>. OBS is never launched, and
    /// nothing is started or stopped.
    /// </summary>
    ObsStreamHealth CheckStreamHealth() => ReadStreamHealth();
    ObsRuntimeSnapshot ReadObsState();
}
