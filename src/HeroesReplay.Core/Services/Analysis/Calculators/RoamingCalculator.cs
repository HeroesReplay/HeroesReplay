using System;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class RoamingCalculator : IFocusCalculator
{
    private readonly AppSettings settings;

    public RoamingCalculator(AppSettings settings)
    {
        this.settings = settings;
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            TimeSpan now = TimeSpan.FromSeconds(second);
            foreach (Unit heroUnit in timeline.AliveHeroesAt(second))
            {
                if (
                    heroUnit.PlayerControlledBy?.HeroUnits == null
                    || heroUnit.PlayerControlledBy.HeroUnits.Count == 0
                )
                {
                    continue;
                }

                if (!timeline.TryGetPoint(heroUnit, second, out Point point))
                {
                    continue;
                }

                Point spawn = heroUnit.PlayerControlledBy.HeroUnits[0].PointBorn;
                if (point.DistanceTo(spawn) <= settings.Spectate.MinDistanceToSpawn)
                {
                    continue;
                }

                timeline.Offer(
                    now,
                    GetType(),
                    heroUnit,
                    heroUnit.PlayerControlledBy,
                    settings.Weights.Roaming,
                    $"{heroUnit.PlayerControlledBy.Character} is roaming"
                );
            }
        }
    }
}
