using System;
using System.Collections.Generic;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Configuration;

public class OBSSettings
{
    public bool Enabled { get; set; }

    /// <summary>Optional path to obs64.exe. Empty = default Program Files install.</summary>
    public string ExecutablePath { get; set; }
    public bool RecordingEnabled { get; set; }

    /// <summary>
    /// When false (the default), HeroesReplay never calls OBS StartStream/StopStream.
    /// Dev VMs must leave this off so connectivity recovery cannot go live.
    /// </summary>
    public bool StreamingEnabled { get; set; }

    /// <summary>
    /// When true, shutdown may close an OBS process this coordinator launched.
    /// A process that was already running is never closed. Default false.
    /// </summary>
    public bool CloseOwnedOnStop { get; set; }

    public bool RecordRequestedReplays { get; set; }
    public string InfoFileName { get; set; }
    public string WebSocketEndpoint { get; set; }
    public string WebSocketPassword { get; set; }
    public string GameSceneName { get; set; }
    public string WaitingSceneName { get; set; }

    /// <summary>
    /// Minimum wait after the previous game exits, before the next replay launches.
    /// Report scenes finish in this time. When they finish first, the waiting scene
    /// stays up for the rest. Zero launches immediately. Default 90 seconds.
    /// </summary>
    public TimeSpan BeforeNextReplay { get; set; } = TimeSpan.FromSeconds(90);
    public IEnumerable<ReportScene> ReportScenes { get; set; }
    public string ReportBrowserCss { get; set; }

    /// <summary>
    /// When true, the match-report browser source hides the Heroes Profile site menu and the event banner above the match.
    /// </summary>
    public bool HideReportHeader { get; set; } = true;
    public IEnumerable<string> RankImagesSourceNames { get; set; }

    public string InfoSourceName { get; set; }

    /// <summary>How long current-replay stays on game-scene from the match clock. Default 45 seconds.</summary>
    public TimeSpan InfoVisibleFor { get; set; } = TimeSpan.FromSeconds(45);
    public string TierDivisionSourceName { get; set; } = "tier-division";
    public string TierRankPointsSourceName { get; set; } = "rank-points";
}
