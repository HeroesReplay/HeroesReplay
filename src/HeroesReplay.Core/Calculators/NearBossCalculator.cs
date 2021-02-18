using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Runner;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearBossCalculator // : IFocusCalculator
    {
        private readonly AppSettings settings;
        private readonly IGameData gameData;

        public NearBossCalculator(AppSettings settings, IGameData gameData)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
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
                                                                               playerPos.Point.DistanceTo(bossPos.Point) < settings.Spectate.MaxDistanceToBoss));

                    if (isNearBoss)
                    {
                        yield return new Focus(
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        settings.Weights.CaptureBeacon,
                        $"{heroUnit.PlayerControlledBy.Character} near {bossUnit.Name} (Boss)");
                    }
                }
            }
        }
    }
}