using HeroesReplay.Core.Models;

using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public class OBSSettings
    {
        public bool Enabled { get; set; }
        public bool RecordingEnabled { get; set; }
        public string RecordingFolderDirectory { get; set; }
        public string WebSocketEndpoint { get; set; }
        public string GameSceneName { get; set; }
        public string WaitingSceneName { get; set; }
        public IEnumerable<ReportScene> ReportScenes { get; set; }
        public IEnumerable<string> RankImagesSourceNames { get; set; }
    }
}