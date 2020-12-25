using System;

namespace HeroesReplay.Core.Shared
{
    public record BrowserSource 
    {
        public string SceneName { get; init; }
        public string SourceUrl { get; init; }
        public TimeSpan DisplayTime { get; init; }
    }
}