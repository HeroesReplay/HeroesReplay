using Heroes.ReplayParser;

using HeroesReplay.Core.Runner;
using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class CaptureBeaconCalculator : IFocusCalculator
    {
        private readonly Settings settings;
        private readonly IGameData gameDataService;

        public CaptureBeaconCalculator(Settings settings, IGameData gameDataService)
        {
            this.settings = settings;
            this.gameDataService = gameDataService;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var heroUnit in replay.Units.Where(u => gameDataService.GetUnitGroup(u.Name) == Unit.UnitGroup.Hero && u.TimeSpanBorn < now && u.TimeSpanDied > now))
            {
                foreach (var captureUnit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == null && unit.Team == null && !settings.Units.IgnoreNames.Any(u => unit.Name.Contains(u))))
                {
                    foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(captureUnit.PointBorn) < 10))
                    {
                        yield return new Focus(GetType(), heroUnit, heroUnit.PlayerControlledBy, settings.Weights.NearCaptureBeacon, $"{heroUnit.PlayerControlledBy.HeroId} near {captureUnit.Name} (CaptureBeacons)");
                    }
                }
            }
        }
    }
}