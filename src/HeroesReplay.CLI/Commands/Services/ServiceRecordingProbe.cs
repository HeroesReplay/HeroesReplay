using System;
using System.Diagnostics;
using System.Threading;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// Stops the OBS recording spectate claimed and left running, for <c>services stop</c> (#318).
/// Call it only after the roles exited, so it is not a second websocket next to the spectator's.
/// It opens OBS only when a claim exists, and never stops the stream.
/// </summary>
internal static class ServiceRecordingProbe
{
    public static OrphanRecordingCheck StopLeftRecording(Func<int, string> processNameOrNull) =>
        OrphanRecording.Stop(
            new RecordingClaimStore(RecordingClaimStore.DefaultPath),
            ObsRunning(),
            pid => ClaimantRunning(pid, processNameOrNull),
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

    /// <summary>
    /// A spectate outside services.json (a manual <c>spectate file</c>) still owns its recording.
    /// This stop command is a heroesreplay process too, so its own pid never counts.
    /// </summary>
    private static bool ClaimantRunning(int pid, Func<int, string> processNameOrNull) =>
        pid > 0
        && pid != Environment.ProcessId
        && ServiceProcessPlan.IsHeroesReplay(processNameOrNull?.Invoke(pid));

    private static bool ObsRunning()
    {
        Process[] processes = Process.GetProcessesByName(ObsLaunchDecision.ProcessName);
        foreach (Process process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }
}
