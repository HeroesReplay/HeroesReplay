namespace HeroesReplay.Core.Shared
{
    public record WeightSettings
    {
        public float Roaming { get; init; }
        public float KillingMinions { get; init; }
        public float NearCaptureBeacon { get; init; }
        public float MercClear { get; init; }
        public float TauntingEmote { get; init; }
        public float TauntingDance { get; init; }
        public float TauntingBStep { get; init; }
        public float DestroyStructure { get; init; }
        public float CampCapture { get; init; }
        public float BossCapture { get; init; }
        public float MapObjective { get; init; }
        public float NearEnemyCore { get; init; }
        public float NearEnemyHero { get; init; }
        public float PlayerDeath { get; init; }
        public float PlayerKill { get; init; }
    }
}