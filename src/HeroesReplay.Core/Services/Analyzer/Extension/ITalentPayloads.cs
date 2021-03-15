using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.Analyzer
{
    public interface ITalentPayloads
    {
        List<TalentsPayload> Create { get; }
        List<TalentsPayload> Update { get; }
        Dictionary<TimeSpan, List<TalentsPayload>> Talents { get; }
    }
}