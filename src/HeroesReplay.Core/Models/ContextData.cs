using System;
using System.Collections.Generic;
using System.IO;

using HeroesReplay.Core.Services.HeroesProfileExtension;

namespace HeroesReplay.Core.Models
{
    public class ContextData
    {
        public LoadedReplay LoadedReplay { get; set; }
        public IReadOnlyDictionary<TimeSpan, Focus> Players { get; set; }
        public IReadOnlyDictionary<TimeSpan, Panel> Panels { get; set; }
        public TimeSpan CoreKilled { get; set; }
        public bool IsCarriedObjectiveMap { get; set; }
        public TimeSpan GatesOpen { get; set; }
        public ITalentPayloads Payloads { get; set; }
        public DateTime Timeloaded { get; set; }
        public TimeSpan? Timer { get; set; }
        public DirectoryInfo Directory { get; set; }
        public IReadOnlyDictionary<int, IReadOnlyCollection<string>> TeamBans { get; set; }
    }
}