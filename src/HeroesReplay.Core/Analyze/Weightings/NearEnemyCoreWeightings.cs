using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearEnemyCoreWeightings : IGameWeightings
    {
        private readonly Settings settings;

        public NearEnemyCoreWeightings(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanDied > now && unit.TimeSpanBorn < now)))
            {
                foreach (Unit enemyUnit in replay.Units.Where(u => u.TimeSpanBorn < now && u.Team != heroUnit.Team && settings.UnitSettings.CoreNames.Any(core => u.Name.Equals(core))))
                {
                    foreach (Position heroPosition in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(enemyUnit.PointBorn) < 20))
                    {
                        yield return (heroUnit, heroUnit.PlayerControlledBy, settings.SpectateWeightSettings.NearEnemyCore, $"{heroUnit.PlayerControlledBy.HeroId} near enemy core: {enemyUnit.Name}");
                    }
                }
            }
        }
    }
}