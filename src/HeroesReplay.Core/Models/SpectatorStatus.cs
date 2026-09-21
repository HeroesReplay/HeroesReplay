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
