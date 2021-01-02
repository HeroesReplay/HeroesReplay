using Heroes.ReplayParser;

using HeroesReplay.Core.Models;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public interface IReplayAnalzer
    {
        IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay);
        IReadOnlyDictionary<TimeSpan, Panel> GetPanels(Replay replay);
        TimeSpan GetEnd(Replay replay);

        bool IsCarriedObjectiveMap(Replay replay);
    }

}