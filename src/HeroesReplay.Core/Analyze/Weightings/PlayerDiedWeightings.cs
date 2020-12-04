using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerDiedWeightings : IGameWeightings
    {
        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var unit in replay.Units.Where(u => u.Name.StartsWith("Hero") && u.TimeSpanDied == now && (u.PlayerKilledBy == null || u.PlayerKilledBy == u.PlayerControlledBy) && u.PlayerControlledBy != null))
            {
                yield return (unit, unit.PlayerControlledBy, Constants.Weights.PlayerDeath, $"{unit.PlayerControlledBy.HeroId} killed by {unit.UnitKilledBy?.Name} in {unit.TimeSpanDied.Value.Subtract(now).TotalSeconds} (death)");
            }
        }
    }
}