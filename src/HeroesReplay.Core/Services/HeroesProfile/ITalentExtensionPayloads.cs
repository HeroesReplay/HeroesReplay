using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public interface ITalentExtensionPayloads
    {
        Queue<HeroesProfileTwitchPayload> Create { get; }
        Dictionary<TimeSpan, List<HeroesProfileTwitchPayload>> Talents { get; }
        Queue<HeroesProfileTwitchPayload> Update { get; }
    }
}