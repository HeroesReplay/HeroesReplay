using System;

namespace HeroesReplay.Core.Models
{
    public record ReportScene 
    {
        public bool Enabled { get; init; }
        public string SceneName { get; init; }
        public Uri SourceUrl { get; init; }
        public string SourceName { get; init; }
        public TimeSpan DisplayTime { get; init; }
    }
}