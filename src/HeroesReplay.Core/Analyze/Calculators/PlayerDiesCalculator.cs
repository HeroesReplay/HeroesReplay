using Heroes.ReplayParser;

using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{

    public class PlayerDiesCalculator : IFocusCalculator
    {
        private readonly Settings settings;
        private readonly IGameData gameData;

        public PlayerDiesCalculator(Settings settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var unit in replay.Units.Where(u => gameData.GetUnitGroup(u.Name) == Unit.UnitGroup.Hero && u.TimeSpanDied == now && (u.PlayerKilledBy == null || u.PlayerKilledBy == u.PlayerControlledBy) && u.PlayerControlledBy != null))
            {
                yield return new Focus(GetType(), unit, unit.PlayerControlledBy, settings.Weights.PlayerDeath, $"{unit.PlayerControlledBy.HeroId} killed by {unit.UnitKilledBy?.Name} in {unit.TimeSpanDied.Value.Subtract(now).TotalSeconds} (death)");
            }
        }
    }
}