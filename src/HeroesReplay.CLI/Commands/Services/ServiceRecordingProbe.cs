using System;
using System.Threading;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// Stops the OBS recording spectate claimed and left running, for <c>services stop</c> (#318).
/// Call it only after the roles exited, so it is not a second websocket next to the spectator's.
/// It opens OBS only when a claim exists and its claimant is dead, and never stops the stream.
/// </summary>
internal static class ServiceRecordingProbe
{
    /// <summary>
    /// A spectate outside services.json (a manual <c>spectate file</c>) still owns its recording:
    /// the claim's pid and start time are checked through <see cref="ProcessTable"/> (#342).
    /// </summary>
    public static OrphanRecordingCheck StopLeftRecording() =>
        OrphanRecording.Stop(
            new RecordingClaimStore(RecordingClaimStore.DefaultPath),
            NamedProcess.IsRunning(ObsLaunchDecision.ProcessName),
            ProcessTable.Find,
            Open,
            DateTimeOffset.UtcNow,
            Thread.Sleep
        );

    private static IObsRecordStopSession Open()
    {
        AppSettings settings = ServiceCollectionExtensions.LoadAppSettings();
        return new ObsWebsocketRecordStopSessionFactory().Open(
            settings.OBS?.WebSocketEndpoint,
            settings.OBS?.WebSocketPassword
        );
    }
}
