using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface ITalentExtensionPayloads
    {
        List<HeroesProfileTwitchPayload> Create { get; }
        List<HeroesProfileTwitchPayload> Update { get; }
        Dictionary<TimeSpan, List<HeroesProfileTwitchPayload>> Talents { get; }
    }
}