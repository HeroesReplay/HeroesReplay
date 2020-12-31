using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record OBSSettings
    {
        public bool Enabled { get; init; }
        public string WebSocketEndpoint { get; init; }
        public string GameSceneName { get; init; }
        public string WaitingSceneName { get; init; }
        public string TierTextSourceName { get; init; }
        public IEnumerable<ReportScene> ReportScenes { get; init; }
        public IEnumerable<string> TierSources { get; init; }
    }
}