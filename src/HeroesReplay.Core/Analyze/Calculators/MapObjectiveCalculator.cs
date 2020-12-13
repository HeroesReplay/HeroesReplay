using Heroes.ReplayParser;

using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class MapObjectiveCalculator : IFocusCalculator
    {
        private readonly Settings settings;
        private readonly IGameData heroesData;

        public MapObjectiveCalculator(Settings settings, IGameData heroesTool)
        {
            this.settings = settings;
            this.heroesData = heroesTool;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var unit in replay.TeamObjectives.SelectMany(to => to).Where(to => to.Player != null && to.TimeSpan == now))
            {
                yield return new Focus(GetType(), unit.Player.HeroUnits.FirstOrDefault(hu => hu.Positions.Any(p => p.TimeSpan == now)), unit.Player, settings.Weights.MapObjective, $"{unit.Player.HeroId} did {unit.TeamObjectiveType} (TeamObjective)");
            }

            foreach (Unit mapUnit in replay.Units.Where(unit => heroesData.GetUnitGroup(unit.Name) == Unit.UnitGroup.MapObjective && unit.TimeSpanDied == now && unit.PlayerKilledBy != null))
            {
                yield return new Focus(GetType(), mapUnit, mapUnit.PlayerKilledBy, settings.Weights.MapObjective, $"{mapUnit.PlayerKilledBy.HeroId} died {mapUnit.Name} (MapObjective)");
            }
        }
    }
}