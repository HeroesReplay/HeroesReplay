﻿using System;

namespace HeroesReplay.Core.Configuration
{
    public record PanelTimesSettings
    {
        public TimeSpan KillsDeathsAssists { get; init; }
        public TimeSpan CarriedObjectives { get; init; }
        public TimeSpan CrowdControlEnemyHeroes { get; init; }
        public TimeSpan DeathDamageRole { get; init; }
        public TimeSpan Talents { get; init; }
        public TimeSpan Experience { get; init; }
        public TimeSpan TimeDeadDeathsSelfSustain { get; init; }
    }
}