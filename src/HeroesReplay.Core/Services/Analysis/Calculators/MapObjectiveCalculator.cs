using System;
using System.Collections.Generic;
using System.Linq;

using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;

using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analysis.Calculators
{
    public class MapObjectiveCalculator : IFocusCalculator
    {
        private readonly IOptions<AppSettings> settings;
        private readonly IGameData gameData;

        public MapObjectiveCalculator(IOptions<AppSettings> settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
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
                        settings.Value.Weights.MapObjective,
                        $"{teamObjective.Player.Character} did {teamObjective.TeamObjectiveType} (TeamObjective)");
                }
            }

            foreach (Unit mapUnit in replay.Units.Where(unit => gameData.GetUnitGroup(unit.Name) == Unit.UnitGroup.MapObjective && unit.TimeSpanDied == now && unit.PlayerKilledBy != null))
            {
                yield return new Focus(
                    GetType(),
                    mapUnit,
                    mapUnit.PlayerKilledBy,
                    settings.Value.Weights.MapObjective,
                    $"{mapUnit.PlayerKilledBy.Character} destroyed {mapUnit.Name} (MapObjective)");
            }
        }
    }
}