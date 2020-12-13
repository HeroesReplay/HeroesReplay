using Heroes.ReplayParser;

using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerKillsCalculator : IFocusCalculator
    {
        private readonly Settings settings;
        private readonly IGameData gameDataService;

        public PlayerKillsCalculator(Settings settings, IGameData gameDataService)
        {
            this.settings = settings;
            this.gameDataService = gameDataService;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            var killers = replay.Units.Where(u => gameDataService.GetUnitGroup(u.Name) == Unit.UnitGroup.Hero && u.TimeSpanDied == now && u.PlayerKilledBy != null).GroupBy(heroUnit => heroUnit.PlayerKilledBy);

            foreach (var killer in killers)
            {
                foreach (var unit in killer)
                {
                    yield return new Focus(GetType(), unit, unit.PlayerKilledBy, settings.Weights.PlayerKill + killer.Count(), $"{killer.Key.HeroId} kills: {unit.PlayerControlledBy.Character}");
                }
            }
        }
    }
}