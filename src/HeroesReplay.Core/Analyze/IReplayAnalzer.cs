using Heroes.ReplayParser;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public interface IReplayAnalzer
    {
        IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay);
        IReadOnlyDictionary<TimeSpan, Panel> GetPanels(Replay replay);
    }

    public record Focus(Type Calculator, Unit Unit, Player Player, float Points, string Description, int Index = 0);

}