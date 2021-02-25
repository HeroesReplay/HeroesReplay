using System;

namespace HeroesReplay.Core.Models
{
    public class ReportScene 
    {
        public bool Enabled { get; set; }
        public string SceneName { get; set; }
        public Uri SourceUrl { get; set; }
        public string SourceName { get; set; }
        public TimeSpan DisplayTime { get; set; }
    }
}