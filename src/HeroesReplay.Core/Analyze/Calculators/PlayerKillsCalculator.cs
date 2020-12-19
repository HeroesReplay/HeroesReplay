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
        private readonly IGameData gameData;

        public PlayerKillsCalculator(Settings settings, IGameData gameDataService)
        {
            this.settings = settings;
            this.gameData = gameDataService;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            var killers = replay.Players.SelectMany(x => x.HeroUnits).Where(u => u.TimeSpanDied == now && u.PlayerKilledBy != null).GroupBy(heroUnit => heroUnit.PlayerKilledBy);

            foreach (var killer in killers)
            {
                foreach (var unit in killer)
                {
                    var shouldFocusUnitDied = unit.PlayerKilledBy.HeroUnits
                        .SelectMany(p => p.Positions)
                        .Where(p => p.TimeSpan.Add(TimeSpan.FromSeconds(2)) >= now && p.TimeSpan.Subtract(TimeSpan.FromSeconds(2)) <= now)
                        .Any(p => p.Point.DistanceTo(unit.PointDied) > 15);

                    // Abathur mines, Fenix Beam, Tyrande W etc etc etc
                    if (shouldFocusUnitDied)
                    {
                        yield return new Focus(GetType(), unit, unit.PlayerControlledBy, settings.Weights.PlayerKill + killer.Count(), $"{killer.Key.HeroId} kills: {unit.PlayerControlledBy.HeroId}");
                    }
                    else
                    {
                        yield return new Focus(GetType(), unit, unit.PlayerKilledBy, settings.Weights.PlayerKill + killer.Count(), $"{killer.Key.HeroId} kills: {unit.PlayerControlledBy.HeroId}");
                    }
                }
            }
        }
    }
}