using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerKilledWeightings : IGameWeightings
    {
        private readonly Settings settings;

        public PlayerKilledWeightings(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
        {
            var killers = replay.Units.Where(u => u.Name.StartsWith("Hero") && u.TimeSpanDied == now && u.PlayerKilledBy != null).GroupBy(heroUnit => heroUnit.PlayerKilledBy);

            foreach (var killer in killers)
            {
                foreach (var unit in killer)
                {
                    yield return (unit, unit.PlayerKilledBy, settings.SpectateWeightSettings.PlayerKill + killer.Count(), $"{killer.Key.HeroId} kills: {unit.PlayerControlledBy.Character}");
                }
            }
        }
    }
}