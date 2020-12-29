using System;

namespace HeroesReplay.Core.Shared
{
    public record ReportScene 
    {
        public string SceneName { get; init; }
        public string SourceUrl { get; init; }
        public string SourceName { get; init; }
        public TimeSpan DisplayTime { get; init; }
    }
}