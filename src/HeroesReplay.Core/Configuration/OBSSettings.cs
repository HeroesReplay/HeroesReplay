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

    public bool RecordRequestedReplays { get; set; }
    public string InfoFileName { get; set; }
    public string WebSocketEndpoint { get; set; }
    public string WebSocketPassword { get; set; }
    public string GameSceneName { get; set; }
    public string WaitingSceneName { get; set; }
    public IEnumerable<ReportScene> ReportScenes { get; set; }
    public string ReportBrowserCss { get; set; }
    public IEnumerable<string> RankImagesSourceNames { get; set; }

    public string InfoSourceName { get; set; }
    public string TierDivisionSourceName { get; set; } = "tier-division";
    public string TierRankPointsSourceName { get; set; } = "rank-points";
}
