using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public class OBSSettings
    {
        public string WebSocketEndpoint { get; init; }
        public string GameSceneName { get; init; }
        public IEnumerable<BrowserSource> BrowserSources { get; init; }
    }
}