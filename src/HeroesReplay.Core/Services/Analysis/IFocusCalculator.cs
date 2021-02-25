using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Models;

namespace HeroesReplay.Core.Services.Analysis
{
    public interface IFocusCalculator
    {
        IEnumerable<Focus> GetFocusPlayers(TimeSpan timeSpan, Replay replay);
    }
}