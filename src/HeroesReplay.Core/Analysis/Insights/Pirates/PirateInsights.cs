using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis.Insights.Pirates;

/// <summary>
/// A Juice Pirates game: a team that skips objectives and teamfights to fly or portal into the
/// enemy base and hit buildings while the defenders are elsewhere. The classic comp is Lt. Morales
/// with Medivac Dropship carrying the team in, Tyrael with Sanctification keeping them alive; the
/// "2.0" version swaps the Medivac for Medivh's portals. Empty when no team played it.
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

    /// <summary>The other team's core fell.</summary>
    public bool Sunk { get; set; }
}
