namespace HeroesReplay.Service.Spectator.Core.Configuration
{
    public class WeightOptions
    {
        public float Roaming { get; set; }

        public float CaptureBeacon { get; set; }

        public float CampClear { get; set; }
        public float CampCapture { get; set; }
        public float BossCapture { get; set; }

        public float Taunt { get; set; }
        public float Dance { get; set; }
        public float BStep { get; set; }

        public float Structure { get; set; }
        public float TownWall { get; set; }
        public float TownMoonWell { get; set; }
        public float TownCannon { get; set; }
        public float TownGate { get; set; }
        public float TownTownHall { get; set; }

        public float MapObjective { get; set; }

        public float NearEnemyCore { get; set; }

        public float NearEnemyHero { get; set; }
        public float NearEnemyHeroOffset { get; set; }
        public float NearEnemyHeroDistanceDivisor { get; set; }

        public float PlayerDeath { get; set; }
        public float PlayerKill { get; set; }

        public float Core { get; set; }
    }
}