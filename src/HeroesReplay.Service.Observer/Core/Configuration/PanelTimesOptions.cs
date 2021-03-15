using System;

namespace HeroesReplay.Service.Spectator.Core.Configuration
{
    public class PanelTimesOptions
    {
        public TimeSpan Talents { get; set; }
        public TimeSpan DeathDamageRole { get; set; }
        public TimeSpan KillsDeathsAssists { get; set; }
        public TimeSpan Experience { get; set; }
        public TimeSpan CarriedObjectives { get; set; }
    }
}