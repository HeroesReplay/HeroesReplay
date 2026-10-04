using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Analysis.Calculators;

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

        // Standing on a beacon is not a capture (#234). The seconds that lead into an owner change
        // to a team are: the channel that takes the camp, watchtower, or point.
        Dictionary<Unit, HashSet<int>> capturing = beacons.ToDictionary(
            beacon => beacon,
            beacon => CaptureSeconds(beacon, timeline.TotalSeconds)
        );

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

                if (
                    FocusActivity.IdleRemoteBody(
                        timeline,
                        heroUnit,
                        point,
                        second,
                        settings.Spectate
                    )
                )
                {
                    continue;
                }

                foreach (Unit beacon in beacons)
                {
                    if (
                        !capturing[beacon].Contains(second)
                        || point.DistanceTo(beacon.PointBorn)
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
                        $"{heroUnit.PlayerControlledBy.Character} captures {beacon.Name} (CaptureBeacons)"
                    );
                }
            }
        }
    }

    /// <summary>How long a capture channel runs before the owner changes.</summary>
    internal static readonly int CaptureLeadSeconds = 6;

    internal static HashSet<int> CaptureSeconds(Unit beacon, int totalSeconds)
    {
        var seconds = new HashSet<int>();
        foreach (
            OwnerChangeEvent change in beacon.OwnerChangeEvents ?? new List<OwnerChangeEvent>()
        )
        {
            if (change?.Team == null)
            {
                continue;
            }

            int at = change.TimeSpanOwnerChanged.FloorSeconds();
            for (int second = Math.Max(0, at - CaptureLeadSeconds); second <= at; second++)
            {
                if (second < totalSeconds)
                {
                    seconds.Add(second);
                }
            }
        }

        return seconds;
    }
}
