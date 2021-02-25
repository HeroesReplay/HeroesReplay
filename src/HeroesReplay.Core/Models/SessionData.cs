using HeroesReplay.Core.Services.HeroesProfile;
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Models
{
    public class SessionData
    {
        public LoadedReplay LoadedReplay { get; set; }
        public IReadOnlyDictionary<TimeSpan, Focus> Players { get; set; }
        public IReadOnlyDictionary<TimeSpan, Panel> Panels { get; set; }
        public TimeSpan CoreKilled { get; set; }
        public bool IsCarriedObjectiveMap { get; set; }
        public TimeSpan GatesOpen { get; set; }
        public ITalentPayloads Payloads { get; set; }
        public string GameType { get; set; }
        public DateTime Timeloaded { get; set; }
        public TimeSpan? Timer { get; set; }
    }
}