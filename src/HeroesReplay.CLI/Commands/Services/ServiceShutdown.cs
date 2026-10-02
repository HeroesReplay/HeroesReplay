using System;
using HeroesReplay.Core.Obs;
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
}
