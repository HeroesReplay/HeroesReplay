using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearEnemyCoreCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;
        private readonly IGameData gameData;

        public NearEnemyCoreCalculator(AppSettings settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanDied > now && unit.TimeSpanBorn < now)))
            {
                foreach (Unit enemyUnit in replay.Units.Where(u => gameData.GetUnitGroup(u.Name) == Unit.UnitGroup.Structures && u.TimeSpanBorn < now && u.Team != heroUnit.Team && gameData.CoreUnits.Any(core => u.Name.Equals(core))))
                {
                    foreach (Position heroPosition in heroUnit.Positions.Where(p => p.TimeSpan.Add(TimeSpan.FromSeconds(2)) >= now && p.TimeSpan.Subtract(TimeSpan.FromSeconds(2)) <= now && p.Point.DistanceTo(enemyUnit.PointBorn) < settings.Spectate.MaxDistanceToCore))
                    {
                        yield return new Focus(GetType(), heroUnit, heroUnit.PlayerControlledBy, settings.Weights.NearEnemyCore, $"{heroUnit.PlayerControlledBy.HeroId} near enemy core: {enemyUnit.Name}");
                    }
                }
            }
        }
    }
}