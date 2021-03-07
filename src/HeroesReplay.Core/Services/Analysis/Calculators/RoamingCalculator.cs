using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analysis.Calculators
{
    public class RoamingCalculator : IFocusCalculator
    {
        private readonly IOptions<AppSettings> settings;

        public RoamingCalculator(IOptions<AppSettings> settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            var aliveUnits = replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanBorn < now && (unit.TimeSpanDied == null || unit.TimeSpanDied > now)));

            foreach (Unit heroUnit in aliveUnits)
            {
                Point spawn = heroUnit.PlayerControlledBy.HeroUnits[0].PointBorn;

                foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(spawn) > settings.Value.Spectate.MinDistanceToSpawn))
                {
                    yield return new Focus(
                        GetType(), 
                        heroUnit, 
                        heroUnit.PlayerControlledBy,
                        settings.Value.Weights.Roaming,
                        $"{heroUnit.PlayerControlledBy.Character} is roaming");
                }
            }
        }
    }
}