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

    /// <summary>
    /// A solo laner alone with one enemy hero for <c>Spectate:SoloLaneDuelTime</c> (#437). As a
    /// fight a 1v1 is 8.09, so any bigger fight elsewhere took the camera from it. At 9.15 it is
    /// worth a 4v4 (9.11). A map objective (9.25), a 4v5 (9.28), a 5v5 (9.4), a death (9.5), a
    /// kill (10) and every second of a death's lead-in still beat it. 0 turns the duel off.
    /// </summary>
    public float SoloLaneDuel { get; set; } = 9.15f;

    public float PlayerDeath { get; set; }

    /// <summary>
    /// What a death's lead-in weighs at its start (#431). It climbs to the death's own weight for the
    /// last <c>Spectate:PastDeathContextTime</c>, so a big enough fight or objective early in the
    /// lead-in keeps the camera. At 8 a 1v1 elsewhere loses to it and a 3v3 wins its first seconds.
    /// </summary>
    public float DeathLeadInFloor { get; set; } = 8.0f;

    /// <summary>
    /// Anything other than a kill or death worth this much while the hero is dead (an objective, a
    /// boss, a town hall, the core, a fight of eight heroes, but not a solo-lane duel) gives that
    /// death the longest lead-in (#431).
    /// </summary>
    public float DeathImpact { get; set; } = 9.0f;

    /// <summary>
    /// How much more than the shot on screen an outcome (<see cref="FocusDwellOutcome"/>) must
    /// weigh to cut it before <c>Spectate:FocusDwellTime</c> (#435). At 0.5 a kill (10) cuts any
    /// fight (at most 9.4), a death (9.5) one up to 9, a map objective (9.25) one up to 8.75 (a
    /// 1v1 is 8.09), and a boss or a town hall (9) one up to 8.5.
    /// </summary>
    public float FocusDwellMargin { get; set; } = 0.5f;

    /// <summary>
    /// Besides a kill or a death and its lead-in, what counts as an outcome that may cut a shot
    /// before <c>Spectate:FocusDwellTime</c> (#435): anything but a fight worth this much, such as
    /// a boss (9), a town hall (9), a map objective (9.25), the push that ends on the core (9.6), or
    /// the core (15). A fight never cuts it, however big, and neither does a solo-lane duel.
    /// </summary>
    public float FocusDwellOutcome { get; set; } = 9.0f;

    public float PlayerKill { get; set; }
    public float KillStreakBonus { get; set; }
    public float PentaKill { get; set; }

    public float Core { get; set; }
}
