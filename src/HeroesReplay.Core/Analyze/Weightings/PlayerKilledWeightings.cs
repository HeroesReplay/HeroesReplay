using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class PlayerKilledWeightings : IGameWeightings
    {
        private readonly GameDataService gameDataService;

        public PlayerKilledWeightings(GameDataService gameDataService)
        {
            this.gameDataService = gameDataService;
        }

        public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
        {
            var killers = replay.Units.Where(u => u.Name.StartsWith("Hero") && u.TimeSpanDied == now && u.PlayerKilledBy != null).GroupBy(heroUnit => heroUnit.PlayerKilledBy);

            foreach (var killer in killers)
            {
                foreach (var unit in killer)
                {
                    yield return (unit, unit.PlayerKilledBy, Constants.Weights.PlayerKill + killer.Count(), $"{killer.Key.HeroId} kills: {unit.PlayerControlledBy.Character}");
                }

            }
        }
    }
}