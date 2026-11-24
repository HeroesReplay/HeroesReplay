using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Pirates;

/// <summary>
/// A Juice Pirates game: a team that skips objectives and teamfights to fly or portal into the
/// enemy base and go structure to structure while the defenders are elsewhere. The classic comp is
/// Lt. Morales with Medivac Dropship carrying the team in, Tyrael with Sanctification keeping them
/// alive; the "2.0" version swaps the Medivac for Medivh's portals. When a team has a carrier but
/// too few raids, <see cref="Carrier"/>, <see cref="Dives"/> and <see cref="Raids"/> are still
/// filled (for tuning) and <see cref="Pirates"/> is false. Empty when neither team has a carrier.
/// </summary>
public sealed record PirateInsights
{
    public bool Pirates { get; set; }

    /// <summary>The pirate team, or null.</summary>
    public int? Team { get; set; }

    /// <summary>The hero who carried the team in: <c>Lt. Morales</c> (Medivac) or <c>Medivh</c> (portals).</summary>
    public string Carrier { get; set; }

    /// <summary>Tyrael on the team took Sanctification.</summary>
    public bool Sanctification { get; set; }

    /// <summary>The pirate team's heroes.</summary>
    public IReadOnlyList<string> Heroes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Separate base dives: several pirates at an enemy fort, keep or core with at most one
    /// defender there.
    /// </summary>
    public int Dives { get; set; }

    /// <summary>
    /// The dives that were raids: the carrier was there and the pirates destroyed buildings.
    /// </summary>
    public int Raids { get; set; }

    /// <summary>Every dive, raid or not, in time order (for tuning and the insights report).</summary>
    public IReadOnlyList<PirateDive> DiveLog { get; set; } = Array.Empty<PirateDive>();

    /// <summary>The pirate team's share of both teams' structure damage (0 to 1).</summary>
    public double StructureDamageShare { get; set; }

    /// <summary>The other team's core fell.</summary>
    public bool Sunk { get; set; }
}

/// <summary>
/// One dive into the enemy base, from its first to its last diving sample: the most pirates there
/// at once, whether the carrier was there, and the towers, forts and keeps it destroyed.
/// </summary>
public sealed record PirateDive(
    TimeSpan Start,
    TimeSpan End,
    int Divers,
    bool Carrier,
    int Structures,
    bool Raid
);
