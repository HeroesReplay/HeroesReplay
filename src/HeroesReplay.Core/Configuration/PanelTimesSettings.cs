using System;

namespace HeroesReplay.Core.Configuration
{
    public class PanelTimesSettings
    {
        public TimeSpan Talents { get; set; }
        public TimeSpan DeathDamageRole { get; set; }
        public TimeSpan KillsDeathsAssists { get; set; }
        public TimeSpan Experience { get; set; }
        public TimeSpan CarriedObjectives { get; set; }
    }
}