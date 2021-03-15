using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Analyzer
{
    public class TalentPayloads : ITalentPayloads
    {
        public List<TalentsPayload> Create { get; set; } = new();
        public List<TalentsPayload> Update { get; set; } = new();
        public Dictionary<TimeSpan, List<TalentsPayload>> Talents { get; set; } = new();
    }
}