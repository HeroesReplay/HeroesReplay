using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerRoamingWeightings : IGameWeightings
    {
        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanBorn < now && (unit.TimeSpanDied == null || unit.TimeSpanDied > now))))
            {
                var spawn = heroUnit.PlayerControlledBy.HeroUnits[0].PointBorn;

                foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(spawn) > 20))
                {
                    yield return (heroUnit, heroUnit.PlayerControlledBy, Constants.Weights.Roaming, $"{heroUnit.PlayerControlledBy.HeroId} is roaming");
                }
            }
        }
    }
}