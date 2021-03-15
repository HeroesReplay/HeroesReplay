using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class KillCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;

        public KillCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IGameData gameData)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
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
                float weight = weightOptions.PlayerKill + Convert.ToSingle(killer.Count());

                foreach (Unit unit in killer)
                {
                    var shouldFocusUnitDied = unit.PlayerKilledBy.HeroUnits
                        .SelectMany(p => p.Positions)
                        .Where(p => p.TimeSpan.Add(TimeSpan.FromSeconds(2)) >= now && p.TimeSpan.Subtract(TimeSpan.FromSeconds(2)) <= now)
                        .Any(p => p.Point.DistanceTo(unit.PointDied) > spectateOptions.MaxDistanceToEnemyKill);

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