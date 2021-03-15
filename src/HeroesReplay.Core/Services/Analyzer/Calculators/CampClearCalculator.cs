using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class CampClearCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;

        public CampClearCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IGameData gameData)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (Unit unit in replay.Units.Where(unit => unit.TimeSpanDied == now && gameData.GetUnitGroup(unit.Name) == Unit.UnitGroup.MercenaryCamp))
            {
                if (unit.PlayerKilledBy != null)
                {
                    // no point focusing if they're so far away
                    // for example an azmodan Dunk or long range ability is going to look weird
                    bool isNearMercs = unit.PlayerKilledBy.HeroUnits.SelectMany(u => u.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(unit.PointDied) < spectateOptions.MaxDistanceToClear)).Any();

                    if (isNearMercs)
                    {
                        yield return new Focus(
                        GetType(),
                        unit,
                        unit.PlayerKilledBy,
                        weightOptions.CampClear,
                        $"{unit.PlayerKilledBy.Character} kills {unit.Name}");
                    }
                }
            }
        }
    }
}