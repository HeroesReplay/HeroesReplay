using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class NearCaptureBeaconCalculator : IFocusCalculator
{
    private readonly AppSettings settings;

    public NearCaptureBeaconCalculator(AppSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        var beacons = new List<Unit>();
        foreach (Unit unit in timeline.Replay.Units)
        {
            if (unit.TimeSpanBorn != TimeSpan.Zero || unit.TimeSpanDied != null)
            {
                continue;
            }

            if (!settings.HeroesToolChest.CaptureContains.Any(name => unit.Name.Contains(name)))
            {
                continue;
            }

            beacons.Add(unit);
        }

        if (beacons.Count == 0)
        {
            return;
        }

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            TimeSpan now = TimeSpan.FromSeconds(second);
            foreach (Unit heroUnit in timeline.AliveHeroesAt(second))
            {
                if (
                    !timeline.TryGetPoint(heroUnit, second, out Point point)
                    || heroUnit.PlayerControlledBy == null
                )
                {
                    continue;
                }

                foreach (Unit beacon in beacons)
                {
                    if (
                        point.DistanceTo(beacon.PointBorn)
                        >= settings.Spectate.MaxDistanceToOwnerChange
                    )
                    {
                        continue;
                    }

                    timeline.Offer(
                        now,
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        settings.Weights.CaptureBeacon,
                        $"{heroUnit.PlayerControlledBy.Character} near {beacon.Name} (CaptureBeacons)"
                    );
                }
            }
        }
    }
}
