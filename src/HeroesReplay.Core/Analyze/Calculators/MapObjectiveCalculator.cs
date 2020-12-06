using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class MapObjectiveCalculator : IFocusCalculator
    {
        private readonly Settings settings;

        public MapObjectiveCalculator(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var unit in replay.TeamObjectives.SelectMany(to => to).Where(to => to.Player != null && to.TimeSpan == now))
            {
                yield return new Focus(this, unit.Player.HeroUnits.FirstOrDefault(hu => hu.Positions.Any(p => p.TimeSpan == now)), unit.Player, settings.Weights.MapObjective, $"{unit.Player.HeroId} did {unit.TeamObjectiveType} (TeamObjective)");
            }

            foreach (Unit mapUnit in replay.Units.Where(unit => (unit.Group == Unit.UnitGroup.MapObjective || unit.Name.StartsWith("BossDuel") || unit.Name.Contains("Vehicle") || settings.Units.MapObjectiveNames.Contains(unit.Name) || unit.Name.EndsWith("CaptureCage")) && unit.TimeSpanDied == now && unit.PlayerKilledBy != null))
            {
                yield return new Focus(this, mapUnit, mapUnit.PlayerKilledBy, settings.Weights.MapObjective, $"{mapUnit.PlayerKilledBy.HeroId} died {mapUnit.Name} (MapObjective)");
            }
        }
    }
}