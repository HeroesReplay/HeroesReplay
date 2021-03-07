using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public interface ITalentPayloads
    {
        List<TalentsPayload> Create { get; }
        List<TalentsPayload> Update { get; }
        Dictionary<TimeSpan, List<TalentsPayload>> Talents { get; }
    }
}