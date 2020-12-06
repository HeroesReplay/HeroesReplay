using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerDiesFocus : IFocusCalculator
    {
        private readonly Settings settings;

        public PlayerDiesFocus(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var unit in replay.Units.Where(u => u.Name.StartsWith("Hero") && u.TimeSpanDied == now && (u.PlayerKilledBy == null || u.PlayerKilledBy == u.PlayerControlledBy) && u.PlayerControlledBy != null))
            {
                yield return new Focus(this, unit, unit.PlayerControlledBy, settings.Weights.PlayerDeath, $"{unit.PlayerControlledBy.HeroId} killed by {unit.UnitKilledBy?.Name} in {unit.TimeSpanDied.Value.Subtract(now).TotalSeconds} (death)");
            }
        }
    }
}