using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearEnemyCoreWeightings : IGameWeightings
    {
        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanDied > now && unit.TimeSpanBorn < now)))
            {
                foreach (Unit enemyUnit in replay.Units.Where(u => u.TimeSpanBorn < now && u.Team != heroUnit.Team && (u.Name.Equals("KingsCore") || u.Name.Equals("VanndarStormpike") || u.Name.Equals("DrekThar"))))
                {
                    foreach (Position heroPosition in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(enemyUnit.PointBorn) < 20))
                    {
                        yield return (heroUnit, heroUnit.PlayerControlledBy, Constants.Weights.NearEnemyCore, $"{heroUnit.PlayerControlledBy.HeroId} near enemy core: {enemyUnit.Name}");
                    }
                }
            }
        }
    }
}