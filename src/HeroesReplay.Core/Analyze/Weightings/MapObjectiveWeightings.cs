using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class MapObjectiveWeightings : IGameWeightings
	{
		public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
		{
			foreach (var unit in replay.TeamObjectives.SelectMany(to => to).Where(to => to.Player != null && to.TimeSpan == now))
			{
				yield return (unit.Player.HeroUnits.FirstOrDefault(hu => hu.Positions.Any(p => p.TimeSpan == now)), unit.Player, Constants.Weights.MapObjective, $"{unit.Player.HeroId} did {unit.TeamObjectiveType} (TeamObjective)");
			}

			foreach (Unit mapUnit in replay.Units.Where(unit => (unit.Group == Unit.UnitGroup.MapObjective || unit.Name.StartsWith("BossDuel") || unit.Name.Contains("Vehicle") || Constants.MapObjectives.Contains(unit.Name) || unit.Name.EndsWith("CaptureCage")) && unit.TimeSpanDied == now && unit.PlayerKilledBy != null))
			{
				yield return (mapUnit, mapUnit.PlayerKilledBy, Constants.Weights.MapObjective, $"{mapUnit.PlayerKilledBy.HeroId} died {mapUnit.Name} (MapObjective)");
			}
		}
	}
}