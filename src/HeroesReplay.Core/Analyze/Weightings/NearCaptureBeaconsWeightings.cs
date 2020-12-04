using Heroes.ReplayParser;

using HeroesReplay.Core.Shared;

using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core
{
    public class NearCaptureBeaconsWeightings : IGameWeightings
	{
		public IEnumerable<(Unit Unit, Player Player, double Points, string Description)> GetPlayers(TimeSpan now, Replay replay)
		{
			foreach (var heroUnit in replay.Units.Where(u => u.Name.StartsWith("Hero") && u.TimeSpanBorn < now && u.TimeSpanDied > now))
			{
				foreach (var captureUnit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero && unit.TimeSpanDied == null && unit.Team == null && !Constants.IgnoreUnits.Any(u => unit.Name.Contains(u))))
				{
					foreach (var position in heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(captureUnit.PointBorn) <= 5))
					{
						yield return (heroUnit, heroUnit.PlayerControlledBy, Constants.Weights.NearCaptureBeacon, $"{heroUnit.PlayerControlledBy.HeroId} near {captureUnit.Name} (CaptureBeacons)");
					}
				}
			}
		}
	}
}