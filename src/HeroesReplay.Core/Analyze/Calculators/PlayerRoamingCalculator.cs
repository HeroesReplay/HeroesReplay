using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerRoamingCalculator : IFocusCalculator
    {
        private readonly Settings settings;

        public PlayerRoamingCalculator(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanBorn < now && (unit.TimeSpanDied == null || unit.TimeSpanDied > now))))
            {
                var spawn = heroUnit.PlayerControlledBy.HeroUnits[0].PointBorn;

                foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(spawn) > 40))
                {
                    yield return new Focus(GetType(), heroUnit, heroUnit.PlayerControlledBy, settings.Weights.Roaming, $"{heroUnit.PlayerControlledBy.HeroId} is roaming");
                }
            }
        }
    }
}