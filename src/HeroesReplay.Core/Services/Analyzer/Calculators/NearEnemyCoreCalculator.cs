using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;
using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class NearEnemyCoreCalculator : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;
        private readonly TrackerEventOptions trackerOptions;

        public NearEnemyCoreCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IOptions<TrackerEventOptions> trackerOptions, IGameData gameData)
        {
            this.gameData = gameData;
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
            this.trackerOptions = trackerOptions.Value;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanDied > now && unit.TimeSpanBorn < now)))
            {
                foreach (Unit core in replay.Units.Where(u => u.Team != heroUnit.Team && gameData.CoreUnits.Any(core => u.Name.Equals(core, StringComparison.OrdinalIgnoreCase))))
                {
                    var nearCore = heroUnit.Positions.Any(p => p.TimeSpan == now && p.Point.DistanceTo(core.PointBorn) <= spectateOptions.MaxDistanceToCore);

                    if (nearCore)
                    {
                        yield return new Focus(
                            GetType(),
                            heroUnit,
                            heroUnit.PlayerControlledBy,
                            weightOptions.NearEnemyCore,
                            $"{heroUnit.PlayerControlledBy.Character} near enemy core: {core.Name}.");
                    }
                }
            }
        }
    }
}