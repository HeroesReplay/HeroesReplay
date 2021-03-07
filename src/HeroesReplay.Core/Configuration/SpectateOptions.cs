
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public class SpectateOptions
    {
        public string VersionSupported { get; set; }
        public int MinDistanceToSpawn { get; set; }
        public int MaxDistanceToCore { get; set; }
        public int MaxDistanceToEnemy { get; set; }
        public int MaxDistanceToObjective { get; set; }
        public int MaxDistanceToOwnerChange { get; set; }
        public int MaxDistanceToEnemyKill { get; set; }
        public int MaxDistanceToClear { get; set; }
        public int MaxDistanceToBoss { get; set; }

        public int RetryTimerCountBeforeForceEnd { get; set; }
        public TimeSpan RetryTimerSleepDuration { get; set; }

        public TimeSpan EndScreenTime { get; set; }
        public TimeSpan PanelDownTime { get; set; }
        public TimeSpan TalentsPanelStartTime { get; set; }
        public TimeSpan WaitingTime { get; set; }

        public TimeSpan PastDeathContextTime { get; set; }
        public TimeSpan PresentDeathContextTime { get; set; }

        public IEnumerable<int> TalentLevels { get; set; }
    }
}