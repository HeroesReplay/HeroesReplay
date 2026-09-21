using System;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Services.Analysis;

/// <summary>
/// Compile-only spike for the per-second unit cell distance calculators read from
/// Heroes.ReplayParser 1.2.18. Not registered in DI. Do not drop that package:
/// Heroes.StormReplayParser stores UnitBorn, UnitDied, and UnitPositions raw and
/// does not build this graph.
/// </summary>
public interface IReplayUnitPositions
{
    /// <summary>
    /// Map cell of <paramref name="unit"/> at <paramref name="time"/>, or null when that
    /// second has no sample. Callers then use <c>Point.DistanceTo</c>.
    /// NearEnemy compares two hero points. NearEnemyCore and NearCaptureBeacon compare
    /// the hero point to <c>PointBorn</c>. NearBoss compares it to the boss position.
    /// Kill and CampClear compare it to <c>PointDied</c>. Roaming compares it to
    /// <c>HeroUnits[0].PointBorn</c>. MapObjective and Emoting only test that a point
    /// exists. VehicleCalculator uses the <c>Positions</c> time span, not this method.
    /// Today this is <c>ReplayTimeline.TryGetPoint</c>.
    /// </summary>
    Point GetUnitPoint(Replay replay, Unit unit, TimeSpan time);
}
