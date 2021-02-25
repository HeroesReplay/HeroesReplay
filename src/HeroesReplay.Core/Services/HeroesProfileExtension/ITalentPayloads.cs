using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfileExtension
{
    public interface ITalentPayloads
    {
        List<ExtensionPayload> Create { get; }
        List<ExtensionPayload> Update { get; }
        Dictionary<TimeSpan, List<ExtensionPayload>> Talents { get; }
    }
}