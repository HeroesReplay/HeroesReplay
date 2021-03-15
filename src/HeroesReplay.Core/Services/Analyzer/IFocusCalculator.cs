using System;
using System.Collections.Generic;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.Analyzer
{
    public interface IFocusCalculator
    {
        IEnumerable<Focus> GetFocusPlayers(TimeSpan timeSpan, Replay replay);
    }
}