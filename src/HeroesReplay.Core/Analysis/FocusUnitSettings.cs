using System.Collections.Generic;

namespace HeroesReplay.Core.Analysis;

public class FocusUnitSettings
{
    public IEnumerable<string> ObjectiveContains { get; set; } = new string[0];

    /// <summary>
    /// Objective units that stand all game (watchtowers, shrines, altars, cages, turn-in points,
    /// temples). Being near one is not activity; it scores only while an enemy hero contests it.
    /// Every other <see cref="ObjectiveContains"/> unit exists only while its objective is live.
    /// </summary>
    public IEnumerable<string> StructureContains { get; set; } = new string[0];

    /// <summary>
    /// Objectives a team escorts (Hanamura's payload). The replay records one position, so the
    /// path is tracked from the escorting heroes (<see cref="Calculators.EscortedPath"/>).
    /// </summary>
    public IEnumerable<string> EscortContains { get; set; } = new string[0];

    public IEnumerable<string> CoreContains { get; set; } = new string[0];

    public IEnumerable<string> CampContains { get; set; } = new string[0];

    public IEnumerable<string> PickupContains { get; set; } = new string[0];
}
