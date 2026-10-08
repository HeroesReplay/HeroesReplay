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
    public const string ProcessName = "obs64";

    public ObsLaunchKind Kind { get; init; }
    public string ExecutablePath { get; init; }
    public string Arguments { get; init; }
    public bool Started { get; init; }
    public string Detail { get; init; }

    /// <summary>
    /// Profile and collection only. OBS 32 has no flag that skips its Crash Detected dialog; the
    /// stale run sentinel behind it is deleted before launch instead (<see cref="ObsCrashSentinel"/>).
    /// </summary>
    public static string ArgumentsFor(string profileName, string collectionName) =>
        "--profile \""
        + ObsNames.Pick(profileName)
        + "\" --collection \""
        + ObsNames.Pick(collectionName)
        + "\"";

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

    public static ObsLaunchDecision Decide(
        bool enabled,
        bool running,
        string path,
        bool exists,
        string profileName = null,
        string collectionName = null
    )
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
            Arguments = ArgumentsFor(profileName, collectionName),
            Detail =
                "OBS is not running. Launch it with the configured profile and scene collection.",
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

    /// <summary>The scene the spectator last asked OBS for (#282).</summary>
    public string SceneDesired { get; init; }

    /// <summary>The program scene: the last one OBS accepted, or the one it last reported.</summary>
    public string SceneActual { get; init; }
    public bool StreamDesired { get; init; }
    public bool StreamActive { get; init; }
    public bool RecordingDesired { get; init; }
    public bool RecordingActive { get; init; }
    public ObsStreamResult Stream { get; init; }

    /// <summary>
    /// Stable code when the stream is desired but was not started, such as
    /// <c>obs.stream_not_armed</c> or <c>obs.profile_mismatch</c>. Null otherwise.
    /// </summary>
    public string StreamBlockedBy { get; init; }
}

public static class ObsDesired
{
    public static bool StreamIsDesired(OBSSettings obs) =>
        obs is { Enabled: true } && SessionMedia.ShouldStream(obs);

    /// <summary>
    /// <paramref name="sceneRequested"/> is the scene this process last put on the program
    /// output. Until it asks for one, a desired stream wants the waiting scene, which is where
    /// the stream starts.
    /// </summary>
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
        ObsStreamResult stream,
        string sceneRequested = null
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
            SceneDesired = sceneRequested ?? (streamDesired ? obs.WaitingSceneName : null),
            SceneActual = sceneActual,
            StreamDesired = streamDesired,
            StreamActive = streamActive,
            RecordingDesired = recordingDesired,
            RecordingActive = recordingActive,
            Stream = stream,
            StreamBlockedBy =
                streamDesired && stream is { Succeeded: false } ? stream.Reason : null,
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
    /// <summary>
    /// Copies the OBS fields of <paramref name="snapshot"/> into <paramref name="status"/>.
    /// True when one of them changed.
    /// </summary>
    public static bool Copy(SpectatorStatus status, ObsRuntimeSnapshot snapshot)
    {
        if (status == null || snapshot == null)
        {
            return false;
        }

        string detail = snapshot.Stream?.Detail ?? snapshot.Launch?.Detail;
        bool changed =
            status.ObsProcessRunning != snapshot.ProcessRunning
            || status.ObsWebsocketIdentified != snapshot.WebsocketIdentified
            || !string.Equals(
                status.ObsSceneDesired,
                snapshot.SceneDesired,
                StringComparison.Ordinal
            )
            || !string.Equals(status.ObsSceneActual, snapshot.SceneActual, StringComparison.Ordinal)
            || status.ObsStreamDesired != snapshot.StreamDesired
            || status.ObsStreamActive != snapshot.StreamActive
            || !string.Equals(
                status.ObsStreamBlockedBy,
                snapshot.StreamBlockedBy,
                StringComparison.Ordinal
            )
            || !string.Equals(status.ObsDetail, detail, StringComparison.Ordinal);
        status.ObsProcessRunning = snapshot.ProcessRunning;
        status.ObsWebsocketIdentified = snapshot.WebsocketIdentified;
        status.ObsSceneDesired = snapshot.SceneDesired;
        status.ObsSceneActual = snapshot.SceneActual;
        status.ObsStreamDesired = snapshot.StreamDesired;
        status.ObsStreamActive = snapshot.StreamActive;
        status.ObsStreamBlockedBy = snapshot.StreamBlockedBy;
        status.ObsDetail = detail;
        return changed;
    }

    /// <summary>
    /// Writes the OBS fields of status.json when one of them differs from what the store holds,
    /// so a scene switch shows at once and not at the watchdog's next tick (#357). The store
    /// writes the whole file through a temp file and a rename. Nothing changed, nothing written.
    /// <paramref name="latest"/> is read under the store's lock, as the watchdog's tick does,
    /// so neither writer puts an older snapshot over a newer one.
    /// </summary>
    public static bool Write(SpectatorStatusStore store, Func<ObsRuntimeSnapshot> latest) =>
        store != null && latest != null && store.PatchIfChanged(status => Copy(status, latest()));

    /// <summary>
    /// Sets the code of a refused recording start and clears it on the next start. The detail
    /// (which profile or collection is active) is in the spectator log and <c>check obs</c>.
    /// </summary>
    public static void CopyRecording(SpectatorStatus status, ObsRecordingResult started)
    {
        if (status == null || started == null)
        {
            return;
        }

        status.ObsRecordBlockedBy = started.Reason;
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
            + status.ObsStreamActive
            + (
                string.IsNullOrWhiteSpace(status.ObsStreamBlockedBy)
                    ? string.Empty
                    : " blocked=" + status.ObsStreamBlockedBy
            )
            + (
                string.IsNullOrWhiteSpace(status.ObsRecordBlockedBy)
                    ? string.Empty
                    : " record blocked=" + status.ObsRecordBlockedBy
            );
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
