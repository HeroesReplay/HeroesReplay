
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Configuration
{
    public record SpectateSettings
    {
       
        public Version VersionSupported { get; init; }
        public int MinDistanceToSpawn { get; init; }
        public int MaxDistanceToCore { get; init; }
        public int MaxDistanceToEnemy { get; init; }
        public int MaxDistanceToObjective { get; init; }
        public int MaxDistanceToOwnerChange { get; init; }
        public int MaxDistanceToEnemyKill { get; init; }
        public int MaxDistanceToClear { get; init; }

        public int RetryTimerCountBeforeForceEnd { get; init; }
        public TimeSpan RetryTimerSleepDuration { get; init; }

        public TimeSpan EndScreenTime { get; init; }
        public TimeSpan PanelDownTime { get; init; }
        public TimeSpan TalentsPanelStartTime { get; init; }
        public TimeSpan WaitingTime { get; init; }

        public TimeSpan PastDeathContextTime { get; init; }
        public TimeSpan PresentDeathContextTime { get; init; }

        public IEnumerable<int> TalentLevels { get; init; }
        
    }
}