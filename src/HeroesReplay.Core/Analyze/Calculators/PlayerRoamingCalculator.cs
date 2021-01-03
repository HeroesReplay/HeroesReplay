using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerRoamingCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;

        public PlayerRoamingCalculator(AppSettings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanBorn < now && (unit.TimeSpanDied == null || unit.TimeSpanDied > now))))
            {
                var spawn = heroUnit.PlayerControlledBy.HeroUnits[0].PointBorn;

                foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(spawn) > settings.Spectate.MinDistanceToSpawn))
                {
                    yield return new Focus(GetType(), heroUnit, heroUnit.PlayerControlledBy, settings.Weights.Roaming, $"{heroUnit.PlayerControlledBy.Character} is roaming");
                }
            }
        }
    }
}