using Heroes.ReplayParser;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public interface IFocusCalculator
    {
        IEnumerable<Focus> GetPlayers(TimeSpan timeSpan, Replay replay);
    }
}