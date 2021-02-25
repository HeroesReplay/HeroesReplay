using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class KillCalculator : IFocusCalculator
    {
        private readonly AppSettings settings;

        public KillCalculator(AppSettings settings)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            var killers = replay.Players.SelectMany(x => x.HeroUnits)
                .Where(u => u.TimeSpanDied == now && u.PlayerKilledBy != null)
                .GroupBy(heroUnit => heroUnit.PlayerKilledBy);

            foreach (IGrouping<Player, Unit> killer in killers)
            {
                float weight = settings.Weights.PlayerKill + Convert.ToSingle(killer.Count());

                foreach (Unit unit in killer)
                {
                    var shouldFocusUnitDied = unit.PlayerKilledBy.HeroUnits
                        .SelectMany(p => p.Positions)
                        .Where(p => p.TimeSpan.Add(TimeSpan.FromSeconds(2)) >= now && p.TimeSpan.Subtract(TimeSpan.FromSeconds(2)) <= now)
                        .Any(p => p.Point.DistanceTo(unit.PointDied) > settings.Spectate.MaxDistanceToEnemyKill);

                    // Abathur mines, Fenix Beam, Tyrande W etc etc etc
                    if (shouldFocusUnitDied)
                    {
                        yield return new Focus(GetType(), unit, unit.PlayerControlledBy, weight, $"{killer.Key.Character} kills {unit.PlayerControlledBy.Character}");
                    }
                    else
                    {
                        yield return new Focus(GetType(), unit, unit.PlayerKilledBy, weight, $"{killer.Key.Character} kills {unit.PlayerControlledBy.Character}");
                    }
                }
            }
        }
    }
}