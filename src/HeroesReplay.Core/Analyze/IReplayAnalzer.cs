using Heroes.ReplayParser;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public interface IReplayAnalzer
    {
        IDictionary<TimeSpan, Focus> GetPlayers(Replay replay);
        IDictionary<TimeSpan, Panel> GetPanels(Replay replay);
    }

    public record Focus(IFocusCalculator Calculator, Unit Unit, Player Player, float Points, string Description, int Index = 0);

}