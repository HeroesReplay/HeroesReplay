using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class RoamingCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;

        public RoamingCalculator(AppSettings settings)
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

                foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(spawn) > settings.Spectate.MinDistanceToSpawn))
                {
                    yield return new Focus(
                        GetType(), 
                        heroUnit, 
                        heroUnit.PlayerControlledBy,
                        settings.Weights.Roaming,
                        $"{heroUnit.PlayerControlledBy.Character} is roaming");
                }
            }
        }
    }
}