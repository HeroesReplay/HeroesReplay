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

        public SessionData(IReadOnlyDictionary<TimeSpan, Focus> players, IReadOnlyDictionary<TimeSpan, Panel> panels, TimeSpan end, bool carried)
        {
            Players = players;
            Panels = panels;
            End = end;
            IsCarriedObjectiveMap = carried;
        }
    }
}