using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerKillsFocus : IFocusCalculator
    {
        private readonly Settings settings;

        public PlayerKillsFocus(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            var killers = replay.Units.Where(u => u.Name.StartsWith("Hero") && u.TimeSpanDied == now && u.PlayerKilledBy != null).GroupBy(heroUnit => heroUnit.PlayerKilledBy);

            foreach (var killer in killers)
            {
                foreach (var unit in killer)
                {
                    yield return new Focus(this, unit, unit.PlayerKilledBy, settings.Weights.PlayerKill + killer.Count(), $"{killer.Key.HeroId} kills: {unit.PlayerControlledBy.Character}");
                }
            }
        }
    }
}