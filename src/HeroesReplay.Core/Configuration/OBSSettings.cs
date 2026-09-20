using System.Collections.Generic;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Configuration;

public class OBSSettings
{
    public bool Enabled { get; set; }
    public bool RecordingEnabled { get; set; }
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
}
