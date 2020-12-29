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
        private readonly IGameData gameData;

        public CaptureBeaconCalculator(Settings settings, IGameData gameData)
        {
            this.settings = settings;
            this.gameData = gameData;
        }

        public IEnumerable<Focus> GetPlayers(TimeSpan now, Replay replay)
        {
            foreach (var heroUnit in replay.Players.SelectMany(x => x.HeroUnits).Where(u => u.TimeSpanBorn < now && u.TimeSpanDied > now))
            {
                foreach (var captureUnit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero && 
                                                                       unit.TimeSpanDied == null && 
                                                                       unit.Team == null && 
                                                                       !settings.HeroesToolChest.IgnoreUnits.Any(ignoreUnit => unit.Name.Contains(ignoreUnit))))
                {
                    var positions = heroUnit.Positions.Where(p => p.TimeSpan == now && p.Point.DistanceTo(captureUnit.PointBorn) < settings.Spectate.MaxDistanceToOwnerChange);

                    foreach (var position in positions)
                    {
                        yield return new Focus(
                            GetType(), 
                            heroUnit, 
                            heroUnit.PlayerControlledBy, 
                            settings.Weights.NearCaptureBeacon, 
                            $"{heroUnit.PlayerControlledBy.HeroId} near {captureUnit.Name} (CaptureBeacons)");
                    }
                }
            }
        }
    }
}