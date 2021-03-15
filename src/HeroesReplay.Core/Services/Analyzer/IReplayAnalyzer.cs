using System;
using System.Collections.Generic;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.Analyzer
{
    public interface IReplayAnalyzer
    {
        IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay);
        IReadOnlyDictionary<TimeSpan, Panel> GetPanels(Replay replay);
        ITalentPayloads GetPayloads(Replay replay);
        TimeSpan GetEnd(Replay replay);
        bool GetIsCarriedObjective(Replay replay);
        TimeSpan GetStart(Replay replay);
        IReadOnlyDictionary<int, IReadOnlyCollection<string>> GetTeamBans(Replay replay);
    }
}