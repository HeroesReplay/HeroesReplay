using Heroes.ReplayParser;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public class SessionData
    {
        public IDictionary<TimeSpan, Focus> Players { get; }
        public IDictionary<TimeSpan, Panel> Panels { get; }
        public TimeSpan End { get; }
        public bool IsCarriedObjectiveMap { get; }

        public SessionData(IDictionary<TimeSpan, Focus> players, IDictionary<TimeSpan, Panel> panels, TimeSpan end, bool carried)
        {
            Players = players;
            Panels = panels;
            End = end;
            IsCarriedObjectiveMap = carried;
        }
    }
}