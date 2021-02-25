using Heroes.ReplayParser;

using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.HeroesProfile;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core
{
    public interface IReplayAnalyzer
    {
        IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay);
        IReadOnlyDictionary<TimeSpan, Panel> GetPanels(Replay replay);
        ITalentPayloads GetPayloads(Replay replay);
        TimeSpan GetEnd(Replay replay);
        bool GetIsCarriedObjective(Replay replay);
        TimeSpan GetStart(Replay replay);
    }
}