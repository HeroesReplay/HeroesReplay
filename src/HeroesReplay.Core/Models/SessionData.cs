using Heroes.ReplayParser;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public class SessionData
	{
		public IDictionary<TimeSpan, (Player Player, double Points, string Description, int Index)> Players { get; }
		public IDictionary<TimeSpan, Panel> Panels { get; }
		public TimeSpan End { get; }
		public bool IsCarriedObjectiveMap { get; } // Doubloons, Gems, Warhead Nukes

        public SessionData(IDictionary<TimeSpan, (Player Player, double Points, string Description, int Index)> focus, IDictionary<TimeSpan, Panel> panels, TimeSpan end, bool carried)
		{
			Players = focus;
			Panels = panels;
			End = end;
			IsCarriedObjectiveMap = carried;
        }
	}
}