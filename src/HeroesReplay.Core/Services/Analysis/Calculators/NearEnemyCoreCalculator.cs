using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;

using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analysis.Calculators
{
    public class NearEnemyCoreCalculator : IFocusCalculator
    {
        private readonly IOptions<AppSettings> settings;
        private readonly IGameData gameData;

        public NearEnemyCoreCalculator(IOptions<AppSettings> settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (Unit heroUnit in replay.Players.SelectMany(p => p.HeroUnits.Where(unit => unit.TimeSpanDied > now && unit.TimeSpanBorn < now)))
            {
                foreach (Unit core in replay.Units.Where(u => u.Team != heroUnit.Team && gameData.CoreUnits.Any(core => u.Name.Equals(core, StringComparison.OrdinalIgnoreCase))))
                {
                    var nearCore = heroUnit.Positions.Any(p => p.TimeSpan == now && p.Point.DistanceTo(core.PointBorn) <= settings.Value.Spectate.MaxDistanceToCore);

                    if (nearCore)
                    {
                        yield return new Focus(
                            GetType(),
                            heroUnit,
                            heroUnit.PlayerControlledBy,
                            settings.Value.Weights.NearEnemyCore,
                            $"{heroUnit.PlayerControlledBy.Character} near enemy core: {core.Name}.");
                    }
                }
            }
        }
    }
}