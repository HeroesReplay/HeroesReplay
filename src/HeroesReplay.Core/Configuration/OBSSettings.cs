using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public class OBSSettings
    {
        public bool Enabled { get; init; }
        public string WebSocketEndpoint { get; init; }
        public string GameSceneName { get; init; }
        public string WaitingSceneName { get; init; }
        public IEnumerable<ReportScene> ReportScenes { get; init; }
    }
}