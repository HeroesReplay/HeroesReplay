using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class CaptureBeaconCalculator : IFocusCalculator
    {
        private readonly Settings settings;

        public CaptureBeaconCalculator(Settings settings)
        {
            this.settings = settings;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var heroUnit in replay.Units.Where(u => u.Name.StartsWith("Hero") && u.TimeSpanBorn < now && u.TimeSpanDied > now))
            {
                foreach (var captureUnit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == null && unit.Team == null && !settings.Units.IgnoreNames.Any(u => unit.Name.Contains(u))))
                {
                    foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(captureUnit.PointBorn) <= 5))
                    {
                        yield return new Focus(this, heroUnit, heroUnit.PlayerControlledBy, settings.Weights.NearCaptureBeacon, $"{heroUnit.PlayerControlledBy.HeroId} near {captureUnit.Name} (CaptureBeacons)");
                    }
                }
            }
        }
    }
}