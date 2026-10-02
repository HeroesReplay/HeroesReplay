using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.TwitchExtension;

namespace HeroesReplay.Core.Analysis;

public interface IReplayAnalyzer
{
    IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay);

    IReadOnlyDictionary<TimeSpan, Focus> GetPlayers(Replay replay, int? priorityPlayerIndex);
    IReadOnlyDictionary<TimeSpan, Panel> GetPanels(Replay replay);
    IReadOnlyList<TimeSpan> GetTalentTimes(Replay replay);
    ExtensionGame GetPayloads(Replay replay);
    TimeSpan GetEnd(Replay replay);
    TimeSpan GetSessionEnd(Replay replay);
    bool GetIsCarriedObjective(Replay replay);
    TimeSpan GetStart(Replay replay);
    IReadOnlyDictionary<int, IReadOnlyCollection<string>> GetTeamBans(Replay replay);
}
