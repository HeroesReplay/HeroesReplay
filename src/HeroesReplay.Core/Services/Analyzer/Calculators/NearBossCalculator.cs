using System;
using System.Collections.Generic;
using System.Linq;

using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;

using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class NearBossCalculator // : IFocusCalculator
    {
        private readonly IGameData gameData;
        private readonly WeightOptions weightOptions;
        private readonly SpectateOptions spectateOptions;

        public NearBossCalculator(IOptions<WeightOptions> weightOptions, IOptions<SpectateOptions> spectateOptions, IGameData gameData)
        {
            this.weightOptions = weightOptions.Value;
            this.spectateOptions = spectateOptions.Value;
            this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (var heroUnit in replay.Players.SelectMany(x => x.HeroUnits).Where(u => u.TimeSpanBorn < now && u.TimeSpanDied > now))
            {
                foreach (Unit bossUnit in replay.Units.Where(u => gameData.GetUnitGroup(u.Name) == Unit.UnitGroup.MercenaryCamp && gameData.BossUnits.Contains(u.Name)))
                {
                    bool isNearBoss = heroUnit.Positions
                           .Where(p => p.TimeSpan == now)
                           .Any(playerPos => bossUnit.Positions.Any(bossPos => playerPos.TimeSpan == bossPos.TimeSpan &&
                                                                               playerPos.Point.DistanceTo(bossPos.Point) < spectateOptions.MaxDistanceToBoss));

                    if (isNearBoss)
                    {
                        yield return new Focus(
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        weightOptions.CaptureBeacon,
                        $"{heroUnit.PlayerControlledBy.Character} near {bossUnit.Name} (Boss)");
                    }
                }
            }
        }
    }
}