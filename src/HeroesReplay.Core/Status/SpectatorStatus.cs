using System;

namespace HeroesReplay.Core.Status;

public sealed class SpectatorStatus
{
    public DateTimeOffset UpdatedAt { get; set; }
    public bool SpectatorRunning { get; set; }
    public bool SnapshotStale { get; set; }
    public string Phase { get; set; } = "Idle";
    public string Timer { get; set; }
    public string GatesOpen { get; set; }
    public string CoreKilled { get; set; }
    public string SessionEnd { get; set; }
    public string Map { get; set; }
    public string ReplayPath { get; set; }
    public string ReplayVersion { get; set; }
    public int? ReplayId { get; set; }

    /// <summary>
    /// True when the viewer typed the Heroes Profile replay id. Predictions stay off
    /// because that id is already public.
    /// </summary>
    public bool SuppressPredictions { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedReplayId { get; set; }
    public int? CompletedWinnerTeam { get; set; }

    /// <summary>
    /// Why the last session ended. Completion fields are set only for <c>VerifiedCompleted</c>.
    /// </summary>
    public string Outcome { get; set; }
    public bool ObsSession { get; set; }
    public bool? ConnectivityOnline { get; set; }
    public string Connectivity { get; set; }
    public bool? ObsProcessRunning { get; set; }
    public bool? ObsWebsocketIdentified { get; set; }
    public string ObsSceneDesired { get; set; }
    public string ObsSceneActual { get; set; }
    public bool? ObsStreamDesired { get; set; }
    public bool? ObsStreamActive { get; set; }

    /// <summary>
    /// Why a desired stream was not started: <c>obs.stream_not_armed</c> (OBS:StreamingEnabled
    /// is true but this machine is not armed), <c>obs.profile_mismatch</c>,
    /// <c>obs.collection_mismatch</c>, or <c>obs.selection_unreadable</c>. Null otherwise.
    /// </summary>
    public string ObsStreamBlockedBy { get; set; }

    /// <summary>
    /// Why the last recording start was refused (a profile or scene collection code). Null
    /// after a recording starts.
    /// </summary>
    public string ObsRecordBlockedBy { get; set; }
    public string ObsDetail { get; set; }
    public SpectatorFocusStatus Focus { get; set; }
}

public sealed class SpectatorFocusStatus
{
    public int Index { get; set; }
    public string Hero { get; set; }
    public string Player { get; set; }
    public string Calculator { get; set; }
    public float Points { get; set; }
    public string Description { get; set; }
}
