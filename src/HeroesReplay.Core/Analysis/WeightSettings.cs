namespace HeroesReplay.Core.Analysis;

public class WeightSettings
{
    public float Roaming { get; set; }

    public float CaptureBeacon { get; set; }

    public float CampClear { get; set; }
    public float Pickup { get; set; }
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

    /// <summary>
    /// A live objective with no fight on it: a hero at a payload, tribute, seed, wave, or boss, or at a
    /// structure an enemy hero is contesting. Below any fight, above roaming.
    /// </summary>
    public float ObjectiveActivity { get; set; } = 6.0f;

    public float NearEnemyCore { get; set; }

    public float EndingCore { get; set; }

    public float NearEnemyHero { get; set; }
    public float NearEnemyHeroOffset { get; set; }
    public float NearEnemyHeroDistanceDivisor { get; set; }

    /// <summary>Added to a fight for each hero in it beyond the first two.</summary>
    public float TeamfightPerHero { get; set; } = 0.17f;

    /// <summary>The most a fight scores, so a death (9.5) or a kill (10) still wins.</summary>
    public float TeamfightMax { get; set; } = 9.4f;

    public float PlayerDeath { get; set; }

    /// <summary>
    /// What a death's lead-in weighs at its start (#431). It climbs to the death's own weight for the
    /// last <c>Spectate:PastDeathContextTime</c>, so a big enough fight or objective early in the
    /// lead-in keeps the camera. At 8 a 1v1 elsewhere loses to it and a 3v3 wins its first seconds.
    /// </summary>
    public float DeathLeadInFloor { get; set; } = 8.0f;

    /// <summary>
    /// Anything other than a kill or death worth this much while the hero is dead (an objective, a
    /// boss, a town hall, the core, a fight of eight heroes) gives that death the longest lead-in
    /// (#431).
    /// </summary>
    public float DeathImpact { get; set; } = 9.0f;

    public float PlayerKill { get; set; }
    public float KillStreakBonus { get; set; }
    public float PentaKill { get; set; }

    public float Core { get; set; }
}
