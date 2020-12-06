using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearEnemyCoreCalculator : IFocusCalculator
    {
        private readonly Settings settings;

        public NearEnemyCoreCalculator(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanDied > now && unit.TimeSpanBorn < now)))
            {
                foreach (Unit enemyUnit in replay.Units.Where(u => u.TimeSpanBorn < now && u.Team != heroUnit.Team && settings.Units.CoreNames.Any(core => u.Name.Equals(core))))
                {
                    foreach (Position heroPosition in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(enemyUnit.PointBorn) < 20))
                    {
                        yield return new Focus(this, heroUnit, heroUnit.PlayerControlledBy, settings.Weights.NearEnemyCore, $"{heroUnit.PlayerControlledBy.HeroId} near enemy core: {enemyUnit.Name}");
                    }
                }
            }
        }
    }
}