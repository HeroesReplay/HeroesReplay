using System;

namespace HeroesReplay.Core.Models;

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
    public bool ObsSession { get; set; }
    public bool? ConnectivityOnline { get; set; }
    public string Connectivity { get; set; }
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
