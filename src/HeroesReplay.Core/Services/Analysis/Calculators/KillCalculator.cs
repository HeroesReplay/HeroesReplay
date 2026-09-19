using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class KillCalculator : IFocusCalculator
{
    private readonly AppSettings settings;

    public KillCalculator(AppSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        var kills = new List<Unit>();
        var counts = new Dictionary<(Player Killer, int Second), int>();

        foreach (Unit unit in timeline.HeroUnits)
        {
            if (!unit.TimeSpanDied.HasValue || unit.PlayerKilledBy == null)
            {
                continue;
            }

            int second = unit.TimeSpanDied.Value.FloorSeconds();
            var key = (unit.PlayerKilledBy, second);
            counts.TryGetValue(key, out int count);
            counts[key] = count + 1;
            kills.Add(unit);
        }

        foreach (Unit unit in kills)
        {
            int second = unit.TimeSpanDied.Value.FloorSeconds();
            float weight = settings.Weights.PlayerKill + counts[(unit.PlayerKilledBy, second)];
            bool longRange = false;

            foreach (Unit killerUnit in unit.PlayerKilledBy.HeroUnits ?? new List<Unit>())
            {
                if (!timeline.TryGetPoint(killerUnit, second, out Point killerPoint))
                {
                    continue;
                }

                if (
                    killerPoint.DistanceTo(unit.PointDied)
                    > settings.Spectate.MaxDistanceToEnemyKill
                )
                {
                    longRange = true;
                    break;
                }
            }

            Player target = longRange ? unit.PlayerControlledBy : unit.PlayerKilledBy;
            timeline.Offer(
                unit.TimeSpanDied.Value,
                GetType(),
                unit,
                target,
                weight,
                $"{unit.PlayerKilledBy.Character} kills {unit.PlayerControlledBy?.Character}"
            );
        }
    }
}
