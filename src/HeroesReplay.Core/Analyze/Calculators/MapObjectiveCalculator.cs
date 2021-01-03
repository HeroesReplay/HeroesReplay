using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class MapObjectiveCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;
        private readonly IGameData heroesData;

        public MapObjectiveCalculator(AppSettings settings, IGameData heroesTool)
        {
            this.settings = settings;
            this.heroesData = heroesTool;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (TeamObjective teamObjective in replay.TeamObjectives.SelectMany(to => to).Where(to => to.Player != null && to.TimeSpan == now))
            {
                var heroUnit = teamObjective.Player.HeroUnits.FirstOrDefault(hu => hu.Positions.Any(p => p.TimeSpan == now));

                if (heroUnit != null)
                {
                    yield return new Focus(
                        GetType(), 
                        heroUnit, 
                        teamObjective.Player, 
                        settings.Weights.MapObjective, 
                        $"{teamObjective.Player.HeroId} did {teamObjective.TeamObjectiveType} (TeamObjective)");
                }
            }

            foreach (Unit mapUnit in replay.Units.Where(unit => heroesData.GetUnitGroup(unit.Name) == Unit.UnitGroup.MapObjective && unit.TimeSpanDied == now && unit.PlayerKilledBy != null))
            {
                yield return new Focus(
                    GetType(),                     
                    mapUnit, 
                    mapUnit.PlayerKilledBy,
                    settings.Weights.MapObjective, $"{mapUnit.PlayerKilledBy.HeroId} destroyed {mapUnit.Name} (MapObjective)");
            }
        }
    }
}