namespace HeroesReplay.Core.Configuration
{
    public record WeightSettings
    {
        public float Roaming { get; init; }        

        public float CaptureBeacon { get; init; }

        public float CampClear { get; init; }
        public float CampCapture { get; init; }
        public float BossCapture { get; init; }

        public float Taunt { get; init; }
        public float Dance { get; init; }
        public float BStep { get; init; }

        public float Structure { get; init; }
        public float TownWall { get; init; }
        public float TownMoonWell { get; init; }
        public float TownCannon { get; init; }
        public float TownGate { get; init; }
        public float TownTownHall { get; init; }
                        
        public float MapObjective { get; init; }
        
        public float NearEnemyCore { get; init; }
        public float NearEnemyHero { get; init; }
        
        public float PlayerDeath { get; init; }
        public float PlayerKill { get; init; }

        public float Core { get; init; }
    }
}