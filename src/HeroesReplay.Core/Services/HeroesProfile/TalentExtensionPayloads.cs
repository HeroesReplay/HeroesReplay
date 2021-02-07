
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class TalentExtensionPayloads : ITalentExtensionPayloads
    {
        public List<HeroesProfileTwitchPayload> Create { get; set; } = new();
        public List<HeroesProfileTwitchPayload> Update { get; set; } = new();
        public Dictionary<TimeSpan, List<HeroesProfileTwitchPayload>> Talents { get; set; } = new();
    }
}