using Heroes.ReplayParser;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public interface IReplayAnalzer
    {
        IDictionary<TimeSpan, (Player Player, double Points, string Description, int Index)> GetPlayers(Replay replay);
        IDictionary<TimeSpan, Panel> GetPanels(Replay replay);
    }
}