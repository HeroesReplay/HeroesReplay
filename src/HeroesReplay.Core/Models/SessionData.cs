﻿using HeroesReplay.Core.Services.HeroesProfile;

using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Models
{
    public class SessionData
    {
        public IReadOnlyDictionary<TimeSpan, Focus> Players { get; init; }
        public IReadOnlyDictionary<TimeSpan, Panel> Panels { get; init; }
        public TimeSpan CoreKilled { get; init; }
        public bool IsCarriedObjectiveMap { get; init; }
        public TimeSpan GatesOpen { get; init; }
        public ITalentExtensionPayloads Payloads { get; init; }
        public int? ReplayId { get; init; }
        public string GameType { get; init; }
        public DateTime Loaded { get; init; }

        public TimeSpan? Timer { get; set; }
    }
}