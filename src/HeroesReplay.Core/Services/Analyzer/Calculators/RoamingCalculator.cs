using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class RoamingCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;

        public RoamingCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IGameData gameData)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            var aliveUnits = replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanBorn < now && (unit.TimeSpanDied == null || unit.TimeSpanDied > now)));

            foreach (Unit heroUnit in aliveUnits)
            {
                Point spawn = heroUnit.PlayerControlledBy.HeroUnits[0].PointBorn;

                foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(spawn) > spectateOptions.MinDistanceToSpawn))
                {
                    yield return new Focus(
                        GetType(), 
                        heroUnit, 
                        heroUnit.PlayerControlledBy,
                        weightOptions.Roaming,
                        $"{heroUnit.PlayerControlledBy.Character} is roaming");
                }
            }
        }
    }
}