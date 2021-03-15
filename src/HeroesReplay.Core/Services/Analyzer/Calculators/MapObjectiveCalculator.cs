using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class MapObjectiveCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;

        public MapObjectiveCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IGameData gameData)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
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
                        weightOptions.MapObjective,
                        $"{teamObjective.Player.Character} did {teamObjective.TeamObjectiveType} (TeamObjective)");
                }
            }

            foreach (Unit mapUnit in replay.Units.Where(unit => gameData.GetUnitGroup(unit.Name) == Unit.UnitGroup.MapObjective && unit.TimeSpanDied == now && unit.PlayerKilledBy != null))
            {
                yield return new Focus(
                    GetType(),
                    mapUnit,
                    mapUnit.PlayerKilledBy,
                    weightOptions.MapObjective,
                    $"{mapUnit.PlayerKilledBy.Character} destroyed {mapUnit.Name} (MapObjective)");
            }
        }
    }
}