using System.Collections.Generic;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Service.Spectator.Core.Configuration
{
    public class ObsOptions
    {
        public bool Enabled { get; set; }
        public bool RecordingEnabled { get; set; }
        public string InfoFileName { get; set; }
        public string WebSocketEndpoint { get; set; }
        public string GameSceneName { get; set; }
        public string WaitingSceneName { get; set; }
        public IEnumerable<ReportScene> ReportScenes { get; set; }
        public IEnumerable<string> RankImagesSourceNames { get; set; }
        public string EntryFileName { get; set; }
        public string InfoSourceName { get; set; }
    }
}