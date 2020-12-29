
using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Shared
{
    public record SpectateSettings
    {
        public int GameLoopsOffset { get; init; }
        public int GameLoopsPerSecond { get; init; }
        public Version MinVersionSupported { get; init; }
        public int MinDistanceToSpawn { get; init; }
        public int MaxDistanceToCore { get; init; }
        public int MaxDistanceToEnemy { get; init; }
        public int MaxDistanceToObjective { get; init; }
        public int MaxDistanceToOwnerChange { get; init; }

        public TimeSpan EndScreenTime { get; init; }
        public TimeSpan PanelRotateTime { get; init; }
        public TimeSpan TalentsPanelStartTime { get; init; }
        public TimeSpan WaitingTime { get; init; }

        public IEnumerable<int> TalentLevels { get; init; }        
    }
}