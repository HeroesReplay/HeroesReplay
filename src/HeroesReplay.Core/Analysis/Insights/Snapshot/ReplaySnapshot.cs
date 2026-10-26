using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Analysis.Insights.Snapshot;

/// <summary>
/// The facts of one replay every insight analyzer reads (#412), parsed once from the replay:
/// players and their end-of-game stats and awards, cores, forts and keeps with their lanes, merc
/// camps and their captures, team levels, hero killing blows, and hero positions. Team 0 is Blue,
/// team 1 is Red.
/// </summary>
public sealed record ReplaySnapshot
{
    public bool Aram { get; init; }

    /// <summary>The map's English name (<c>EnglishMapNames</c>), whatever the replay's game language.</summary>
    public string Map { get; init; }

    public TimeSpan GatesOpen { get; init; }

    /// <summary>The core's death, or the replay's length when no core died.</summary>
    public TimeSpan End { get; init; }

    /// <summary>The team whose core died, or null when the replay has no core death.</summary>
    public int? CoreLostBy { get; init; }

    public IReadOnlyList<SnapshotPlayer> Players { get; init; } = Array.Empty<SnapshotPlayer>();

    /// <summary>Each team's core position (team → point).</summary>
    public IReadOnlyDictionary<int, MapPoint> Cores { get; init; } =
        new Dictionary<int, MapPoint>();

    /// <summary>Forts and keeps (<c>TownStructureInit</c>): their team, lane and position.</summary>
    public IReadOnlyList<Town> Towns { get; init; } = Array.Empty<Town>();

    /// <summary>Forts and keeps destroyed (<c>TownStructureDeath</c>).</summary>
    public IReadOnlyList<TownLoss> TownLosses { get; init; } = Array.Empty<TownLoss>();

    /// <summary>Towers destroyed (team, time): <c>TownCannonTower</c> structure units that died.</summary>
    public IReadOnlyList<TowerLoss> TowerLosses { get; init; } = Array.Empty<TowerLoss>();

    /// <summary>Merc camps (<c>JungleCampInit</c>) and their positions.</summary>
    public IReadOnlyList<Camp> Camps { get; init; } = Array.Empty<Camp>();

    /// <summary>Camp captures (<c>JungleCampCapture</c>).</summary>
    public IReadOnlyList<CampCapture> CampCaptures { get; init; } = Array.Empty<CampCapture>();

    /// <summary>When each team reached each level (team → level → time).</summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<int, TimeSpan>> TeamLevels { get; init; } =
        new Dictionary<int, IReadOnlyDictionary<int, TimeSpan>>();

    public IHeroPositions Positions { get; init; }

    /// <summary>Hero killing blows by players, in time order (#512).</summary>
    public IReadOnlyList<KillingBlow> KillingBlows { get; init; } = Array.Empty<KillingBlow>();

    /// <summary>Boss camps captured (#528), in time order.</summary>
    public IReadOnlyList<BossCapture> BossCaptures { get; init; } = Array.Empty<BossCapture>();

    /// <summary>Every hero death and where it happened (#528), in time order.</summary>
    public IReadOnlyList<HeroDeath> HeroDeaths { get; init; } = Array.Empty<HeroDeath>();

    public static string TeamName(int team) => team == 0 ? "Blue" : "Red";
}

/// <summary>One player: their catalog hero (English name), team and end-of-game stats.</summary>
public sealed record SnapshotPlayer
{
    /// <summary>The player's index in the replay's player list.</summary>
    public int Index { get; init; }

    public int Team { get; init; }

    /// <summary>The catalog hero, or null when the catalog has no match.</summary>
    public Hero Hero { get; init; }

    /// <summary>The hero's English name from the catalog, or null.</summary>
    public string HeroName => Hero?.Name;

    public bool IsAi { get; init; }

    public PlayerScore Score { get; init; } = new();

    /// <summary>The talents picked, as the replay names them.</summary>
    public IReadOnlyList<string> Talents { get; init; } = Array.Empty<string>();
}

/// <summary>The end-of-game score screen for one player.</summary>
public sealed record PlayerScore
{
    public int Kills { get; init; }
    public int Deaths { get; init; }
    public int Assists { get; init; }
    public int Takedowns { get; init; }
    public int HeroDamage { get; init; }
    public int SiegeDamage { get; init; }
    public int MinionDamage { get; init; }
    public int Healing { get; init; }
    public int Experience { get; init; }
    public int HighestKillStreak { get; init; }

    /// <summary>The game's own MVP award. A player on either team can earn it.</summary>
    public bool Mvp { get; init; }

    /// <summary>The score screen's multikills (two or more hero kills in quick succession).</summary>
    public int Multikill { get; init; }

    public int ClutchHeals { get; init; }

    public int Vengeances { get; init; }

    public int Escapes { get; init; }

    public int TeamfightEscapes { get; init; }

    public int OutnumberedDeaths { get; init; }

    public int Protection { get; init; }

    public TimeSpan CrowdControl { get; init; }

    public TimeSpan OnFire { get; init; }

    /// <summary>The game's end-of-match awards by <c>MatchAwardType</c> name (#512): <c>HatTrick</c>, <c>MostImmortalDamage</c>.</summary>
    public IReadOnlyList<string> Awards { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The score screen's values the parser does not map (#512), such as the map objective's
    /// <c>CurseDamageDone</c>, <c>TimeOnPoint</c> or <c>GardenSeedsCollectedByPlayer</c>.
    /// </summary>
    public IReadOnlyDictionary<string, int> MapValues { get; init; } =
        new Dictionary<string, int>();

    public bool HasAward(string award) => Awards.Contains(award, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A hero killing blow (#512): one of the killer's own hero units killed an enemy player's hero
/// unit (the <c>TeamKillDeaths</c> rule). <see cref="Second"/> is replay time.
/// </summary>
public sealed record KillingBlow(int Second, int KillerIndex);

/// <summary>A fort (level 2) or keep (level 3).</summary>
public sealed record Town(int Id, int Team, int Lane, MapPoint Point);

/// <summary>A fort or keep destroyed: <c>TownTownHallL2</c> is a fort, <c>L3</c> a keep.</summary>
public sealed record TownLoss(int TownId, int Team, bool Keep, TimeSpan Time);

/// <summary>A tower destroyed.</summary>
public sealed record TowerLoss(int Team, TimeSpan Time);

public sealed record Camp(int Id, MapPoint Point);

/// <summary>A camp captured by <see cref="Team"/>.</summary>
public sealed record CampCapture(int CampId, string CampType, int Team, TimeSpan Time);

/// <summary>
/// A boss camp captured by <see cref="Team"/> (#528): the <c>JungleCampCapture</c> of a camp whose
/// type names a boss, at the camp's <c>JungleCampInit</c> position. <see cref="KillerIndex"/> is the
/// player whose unit landed the boss's killing blow, or -1 when the replay names none. That player
/// can be on the other team: a killed boss is captured by standing in its circle.
/// </summary>
public sealed record BossCapture(
    int CampId,
    int Team,
    TimeSpan Time,
    MapPoint Point,
    int KillerIndex
);

/// <summary>A player's hero unit died at <see cref="Point"/> (#528).</summary>
public sealed record HeroDeath(int PlayerIndex, int Team, TimeSpan Time, MapPoint Point);
