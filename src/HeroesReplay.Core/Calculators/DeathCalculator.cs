using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class DeathCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;
        private readonly IGameData gameData;

        public DeathCalculator(AppSettings settings, IGameData gameData)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null) 
                throw new ArgumentNullException(nameof(replay));

            foreach (var unit in replay.Units.Where(u => gameData.GetUnitGroup(u.Name) == Unit.UnitGroup.Hero && u.TimeSpanDied == now && (u.PlayerKilledBy == null || u.PlayerKilledBy == u.PlayerControlledBy) && u.PlayerControlledBy != null))
            {
                yield return new Focus(
                    GetType(),
                    unit, 
                    unit.PlayerControlledBy, 
                    settings.Weights.PlayerDeath,
                    $"{unit.PlayerControlledBy.Character} killed by {unit.UnitKilledBy?.Name}");
            }
        }
    }
}