using HeroesReplay.Core.Services.HeroesProfile;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Models
{
    public class SessionData
    {
        public IReadOnlyDictionary<TimeSpan, Focus> Players { get; }
        public IReadOnlyDictionary<TimeSpan, Panel> Panels { get; }
        public TimeSpan End { get; }
        public bool IsCarriedObjectiveMap { get; }
        public TimeSpan GatesOpen { get; }
        public ITalentExtensionPayloads Payloads { get; }
        public int? ReplayId { get; }
        public string GameType { get; }

        public SessionData(int? replayId, string gameType, ITalentExtensionPayloads payloads, IReadOnlyDictionary<TimeSpan, Focus> players, IReadOnlyDictionary<TimeSpan, Panel> panels, TimeSpan start, TimeSpan end, bool carried)
        {
            GameType = gameType;
            ReplayId = replayId;
            Payloads = payloads;
            Players = players;
            Panels = panels;
            End = end;
            IsCarriedObjectiveMap = carried;
            GatesOpen = start;
        }
    }
}