using System;
using HeroesReplay.Core.Obs;
using HeroesReplay.Core.Obs.Recording;
using HeroesReplay.Core.ServiceHost;

namespace HeroesReplay.CLI.Commands.Services;

/// <summary>
/// What <c>services stop</c> does to processes, the game, and OBS. Tests replace each step.
/// </summary>
public sealed class ServiceShutdown
{
    public static readonly TimeSpan DefaultGracefulWait = TimeSpan.FromSeconds(20);

    public Func<int, string> ProcessNameOrNull { get; set; }
    public Func<int, ServiceProcessProbe> Probe { get; set; }
    public Action<int> Kill { get; set; }
    public Action RequestGracefulStop { get; set; }
    public TimeSpan GracefulWait { get; set; } = DefaultGracefulWait;
    public Action<TimeSpan> Wait { get; set; }
    public Action ClearStopFile { get; set; }

    /// <summary>Closes Heroes of the Storm. True when no game process is left.</summary>
    public Func<bool> CloseGame { get; set; }

    public Func<ObsShutdownPlan, ObsStreamResult> ConfirmStream { get; set; }

    /// <summary>Read-only OBS stream state. Called only after every role has exited.</summary>
    public Func<ServiceStreamCheck> ReadStream { get; set; }

    /// <summary>
    /// Stops the OBS recording spectate started and left running (#318). Called only after every
    /// role has exited, after <see cref="ReadStream"/>. It never stops the stream.
    /// </summary>
    public Func<OrphanRecordingCheck> StopSpectateRecording { get; set; }

    /// <summary>
    /// Called after the stop file is down: waits for a running supervisor to exit, killing it
    /// after its budget. Null when no supervisor was running.
    /// </summary>
    public Func<ServiceRoleStop> StopSupervisor { get; set; }
}
