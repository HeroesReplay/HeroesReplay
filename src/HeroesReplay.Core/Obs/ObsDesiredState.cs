using System;
using System.Collections.Generic;
using HeroesReplay.Core.Status;

namespace HeroesReplay.Core.Obs;

public enum ObsLaunchKind
{
    Skipped,
    AlreadyRunning,
    Missing,
    Launch,
}

/// <summary>
/// What to do about obs64. <see cref="ObsLaunchKind.Launch"/> is only a decision until
/// <see cref="IObsProcess"/> starts it. A process that is already running is not owned.
/// </summary>
public sealed record ObsLaunchDecision
{
    public const string ProfileName = "HeroesReplay";
    public const string CollectionName = "HeroesReplay";
    public const string ProcessName = "obs64";

    public ObsLaunchKind Kind { get; init; }
    public string ExecutablePath { get; init; }
    public string Arguments { get; init; }
    public bool Started { get; init; }
    public string Detail { get; init; }

    public static string ArgumentsForHeroesReplay() =>
        "--profile \"" + ProfileName + "\" --collection \"" + CollectionName + "\"";

    public static string ResolveExecutable(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "obs-studio",
            "bin",
            "64bit",
            "obs64.exe"
        );
    }

    public static ObsLaunchDecision Decide(bool enabled, bool running, string path, bool exists)
    {
        if (!enabled)
        {
            return new ObsLaunchDecision
            {
                Kind = ObsLaunchKind.Skipped,
                Detail = "OBS is disabled.",
            };
        }

        if (running)
        {
            return new ObsLaunchDecision
            {
                Kind = ObsLaunchKind.AlreadyRunning,
                ExecutablePath = path,
                Detail = "OBS is already running. This coordinator will not adopt the process.",
            };
        }

        if (string.IsNullOrWhiteSpace(path) || !exists)
        {
            return new ObsLaunchDecision
            {
                Kind = ObsLaunchKind.Missing,
                ExecutablePath = path,
                Detail = "OBS is not running and the executable was not found.",
            };
        }

        return new ObsLaunchDecision
        {
            Kind = ObsLaunchKind.Launch,
            ExecutablePath = path,
            Arguments = ArgumentsForHeroesReplay(),
            Detail = "OBS is not running. Launch it with the HeroesReplay profile and collection.",
        };
    }
}

public sealed record ObsShutdownPlan
{
    public bool StopStream { get; init; }
    public bool CloseProcess { get; init; }
    public string Detail { get; init; }

    public static ObsShutdownPlan For(
        bool ownsProcess,
        bool closeOwnedProcess,
        bool streamMayBeActive
    )
    {
        bool close = ownsProcess && closeOwnedProcess;
        string detail = ownsProcess
            ? close
                ? "Stop the stream, then close the OBS process this coordinator started."
                : "This coordinator started OBS. Close-on-stop is off, so the process stays up."
            : "Stop the stream and leave OBS running. This coordinator did not start the process.";
        return new ObsShutdownPlan
        {
            StopStream = streamMayBeActive,
            CloseProcess = close,
            Detail = detail,
        };
    }
}

public sealed class ObsShutdownResult
{
    public ObsShutdownPlan Plan { get; init; }
    public ObsStreamResult Stream { get; init; }
}

/// <summary>
/// Desired versus actual OBS state. The status path can copy this; nothing here writes a file.
/// </summary>
public sealed record ObsRuntimeSnapshot
{
    public bool ProcessDesired { get; init; }
    public bool ProcessRunning { get; init; }
    public bool ProcessOwned { get; init; }
    public ObsLaunchDecision Launch { get; init; }
    public bool WebsocketDesired { get; init; }
    public bool WebsocketIdentified { get; init; }
    public string SceneDesired { get; init; }
    public string SceneActual { get; init; }
    public bool StreamDesired { get; init; }
    public bool StreamActive { get; init; }
    public bool RecordingDesired { get; init; }
    public bool RecordingActive { get; init; }
    public ObsStreamResult Stream { get; init; }
}

public static class ObsDesired
{
    public static bool StreamIsDesired(OBSSettings obs) =>
        obs is { Enabled: true } && SessionMedia.ShouldStream(obs);

    public static ObsRuntimeSnapshot Capture(
        OBSSettings obs,
        bool processRunning,
        bool processOwned,
        ObsLaunchDecision launch,
        bool identified,
        string sceneActual,
        bool streamActive,
        bool recordingDesired,
        bool recordingActive,
        ObsStreamResult stream
    )
    {
        bool enabled = obs?.Enabled == true;
        bool streamDesired = StreamIsDesired(obs);
        return new ObsRuntimeSnapshot
        {
            ProcessDesired = enabled,
            ProcessRunning = processRunning,
            ProcessOwned = processOwned,
            Launch = launch,
            WebsocketDesired = enabled,
            WebsocketIdentified = identified,
            SceneDesired = streamDesired ? obs.WaitingSceneName : null,
            SceneActual = sceneActual,
            StreamDesired = streamDesired,
            StreamActive = streamActive,
            RecordingDesired = recordingDesired,
            RecordingActive = recordingActive,
            Stream = stream,
        };
    }
}

public sealed class ObsBackoff
{
    public static ObsBackoff Default { get; } =
        new(attempts: 4, first: TimeSpan.FromSeconds(1), cap: TimeSpan.FromSeconds(8));

    private readonly int attempts;
    private readonly TimeSpan first;
    private readonly TimeSpan cap;

    public ObsBackoff(int attempts, TimeSpan first, TimeSpan cap)
    {
        this.attempts = attempts < 1 ? 1 : attempts;
        this.first = first < TimeSpan.Zero ? TimeSpan.Zero : first;
        this.cap = cap < this.first ? this.first : cap;
    }

    public IReadOnlyList<TimeSpan> Delays() => Delays(attempts, first, cap);

    public static IReadOnlyList<TimeSpan> Delays(int attempts, TimeSpan first, TimeSpan cap)
    {
        int waits = attempts < 1 ? 0 : attempts - 1;
        if (first < TimeSpan.Zero)
        {
            first = TimeSpan.Zero;
        }

        if (cap < first)
        {
            cap = first;
        }

        var delays = new List<TimeSpan>(waits);
        TimeSpan delay = first;
        for (int i = 0; i < waits; i++)
        {
            delays.Add(delay);
            long doubled = delay.Ticks > long.MaxValue / 2 ? long.MaxValue : delay.Ticks * 2;
            TimeSpan next = doubled == long.MaxValue ? cap : TimeSpan.FromTicks(doubled);
            delay = next > cap ? cap : next;
        }

        return delays;
    }

    public static ObsStreamResult Run(
        IReadOnlyList<TimeSpan> delays,
        Func<ObsStreamResult> attempt,
        Action<TimeSpan> wait
    )
    {
        if (attempt == null)
        {
            throw new ArgumentNullException(nameof(attempt));
        }

        ObsStreamResult result = attempt();
        if (delays == null)
        {
            return result;
        }

        foreach (TimeSpan delay in delays)
        {
            if (
                result != null
                && (result.Succeeded || result.Failure == ObsOutputFailure.NotRequested)
            )
            {
                return result;
            }

            wait?.Invoke(delay);
            result = attempt();
        }

        return result;
    }
}

public static class ObsStatus
{
    public static void Copy(SpectatorStatus status, ObsRuntimeSnapshot snapshot)
    {
        if (status == null || snapshot == null)
        {
            return;
        }

        status.ObsProcessRunning = snapshot.ProcessRunning;
        status.ObsWebsocketIdentified = snapshot.WebsocketIdentified;
        status.ObsSceneDesired = snapshot.SceneDesired;
        status.ObsSceneActual = snapshot.SceneActual;
        status.ObsStreamDesired = snapshot.StreamDesired;
        status.ObsStreamActive = snapshot.StreamActive;
        status.ObsDetail = snapshot.Stream?.Detail ?? snapshot.Launch?.Detail;
    }

    public static string Describe(SpectatorStatus status)
    {
        if (status?.ObsStreamDesired == null)
        {
            return null;
        }

        return "obs process="
            + status.ObsProcessRunning
            + " websocket="
            + status.ObsWebsocketIdentified
            + " scene="
            + (status.ObsSceneActual ?? status.ObsSceneDesired ?? "-")
            + " stream desired="
            + status.ObsStreamDesired
            + " active="
            + status.ObsStreamActive;
    }
}

public static class ObsServiceStop
{
    public static ObsShutdownPlan PlanForServicesCommand() =>
        ObsShutdownPlan.For(ownsProcess: false, closeOwnedProcess: false, streamMayBeActive: true);

    /// <summary>
    /// The services command does not own OBS and must not open a second websocket.
    /// The spectator coordinator confirms the stream is inactive.
    /// </summary>
    public static ObsStreamResult DelegateToSpectator(ObsShutdownPlan plan)
    {
        if (plan == null || plan.CloseProcess)
        {
            return ObsStreamResult.Failed(
                ObsOutputFailure.NotOwned,
                "Refusing to close an OBS process this coordinator does not own."
            );
        }

        return ObsStreamResult.NotRequested(
            "Stream stop is confirmed by the spectator coordinator. This command does not open a websocket."
        );
    }
}
