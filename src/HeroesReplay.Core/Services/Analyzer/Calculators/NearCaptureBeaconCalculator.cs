using System;
using System.Collections.Generic;
using System.Linq;

using Heroes.ReplayParser;

using HeroesReplay.Core.Configuration;

using Microsoft.Extensions.Options;

namespace HeroesReplay.Core.Services.Analyzer.Calculators
{
    public class NearCaptureBeaconCalculator : IFocusCalculator
    {
        private readonly IOptions<HeroesToolChestOptions> heroesToolChestOptions;
        private readonly IOptions<SpectateOptions> spectateOptions;
        private readonly IOptions<WeightOptions> weightOptions;

        public NearCaptureBeaconCalculator(
            IOptions<HeroesToolChestOptions> heroesToolChestOptions, 
            IOptions<SpectateOptions> spectateOptions,
            IOptions<WeightOptions> weightOptions)
        {
            this.heroesToolChestOptions = heroesToolChestOptions;
            this.spectateOptions = spectateOptions;
            this.weightOptions = weightOptions;
        }

        public IEnumerable<Focus> GetFocusPlayers(TimeSpan now, Replay replay)
        {
            if (replay == null)
                throw new ArgumentNullException(nameof(replay));

            foreach (var heroUnit in replay.Players.SelectMany(x => x.HeroUnits).Where(u => u.TimeSpanBorn < now && u.TimeSpanDied > now))
            {
                foreach (var captureUnit in replay.Units.Where(unit => unit.TimeSpanBorn == TimeSpan.Zero &&
                                                                       unit.TimeSpanDied == null &&
                                                                       heroesToolChestOptions.Value.CaptureContains.Any(captureName => unit.Name.Contains(captureName))))
                {
                    var positions = heroUnit.Positions.Where(p => p.TimeSpan == now &&
                                                                  p.Point.DistanceTo(captureUnit.PointBorn) < spectateOptions.Value.MaxDistanceToOwnerChange);

                    /*
                     * This needs to be broken down into seperate capture beacon calculators:
                     * Someone going near a capture beacon can be irrelevant:
                     * - Are there defender mercs at the beacon? Its a merc camp
                     * - What 'type' of capture beacon? 
                     * - Is it the volskaya capture 'slab' objective? (interest)
                     * - Is it the dragon shire or braxis 'capture points'? (interest)
                     * - Is it an 'empty' merc camp? (no interest)
                     */

                    foreach (var position in positions)
                    {
                        yield return new Focus(
                            GetType(),
                            heroUnit,
                            heroUnit.PlayerControlledBy,
                            weightOptions.Value.CaptureBeacon,
                            $"{heroUnit.PlayerControlledBy.Character} near {captureUnit.Name} (CaptureBeacons)");
                    }
                }
            }
        }
    }
}