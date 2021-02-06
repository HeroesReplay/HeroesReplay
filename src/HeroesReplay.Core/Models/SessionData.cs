using HeroesReplay.Core.Services.HeroesProfile;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Models
{
    public class SessionData
    {
        public IReadOnlyDictionary<TimeSpan, Focus> Players { get; }
        public IReadOnlyDictionary<TimeSpan, Panel> Panels { get; }
        public TimeSpan End { get; }
        public bool IsCarriedObjectiveMap { get; }
        public TimeSpan GatesOpen { get; }
        public ITalentExtensionPayloads Payloads { get; }

        public SessionData(ITalentExtensionPayloads payloads, IReadOnlyDictionary<TimeSpan, Focus> players, IReadOnlyDictionary<TimeSpan, Panel> panels, TimeSpan start, TimeSpan end, bool carried)
        {
            Payloads = payloads;
            Players = players;
            Panels = panels;
            End = end;
            IsCarriedObjectiveMap = carried;
            GatesOpen = start;
        }
    }
}