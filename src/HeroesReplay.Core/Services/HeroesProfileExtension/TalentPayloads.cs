
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Services.HeroesProfile
{
    public class TalentPayloads : ITalentPayloads
    {
        public List<ExtensionPayload> Create { get; set; } = new();
        public List<ExtensionPayload> Update { get; set; } = new();
        public Dictionary<TimeSpan, List<ExtensionPayload>> Talents { get; set; } = new();
    }
}