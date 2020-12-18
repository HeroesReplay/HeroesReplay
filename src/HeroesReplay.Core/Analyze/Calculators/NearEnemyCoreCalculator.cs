using Heroes.ReplayParser;

using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearEnemyCoreCalculator : IFocusCalculator
    {
        private readonly Settings settings;
        private readonly IGameData gameData;

        public NearEnemyCoreCalculator(Settings settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanDied > now && unit.TimeSpanBorn < now)))
            {
                foreach (Unit enemyUnit in replay.Units.Where(u => u.TimeSpanBorn < now && u.Team != heroUnit.Team && gameData.CoreNames.Any(core => u.Name.Equals(core))))
                {
                    foreach (Position heroPosition in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(enemyUnit.PointBorn) < 20))
                    {
                        yield return new Focus(GetType(), heroUnit, heroUnit.PlayerControlledBy, settings.Weights.NearEnemyCore, $"{heroUnit.PlayerControlledBy.HeroId} near enemy core: {enemyUnit.Name}");
                    }
                }
            }
        }
    }
}