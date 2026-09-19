using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class NearEnemyCoreCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public NearEnemyCoreCalculator(AppSettings settings, IGameData gameData)
    {
        this.settings = settings;
        this.gameData = gameData;
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        var cores = new List<Unit>();
        foreach (Unit unit in timeline.Replay.Units)
        {
            if (
                gameData.CoreUnits.Any(core =>
                    unit.Name.Equals(core, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                cores.Add(unit);
            }
        }

        if (cores.Count == 0)
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

                foreach (Unit core in cores)
                {
                    if (core.Team == heroUnit.Team)
                    {
                        continue;
                    }

                    if (point.DistanceTo(core.PointBorn) > settings.Spectate.MaxDistanceToCore)
                    {
                        continue;
                    }

                    timeline.Offer(
                        now,
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        settings.Weights.NearEnemyCore,
                        $"{heroUnit.PlayerControlledBy.Character} near enemy core: {core.Name}."
                    );
                }
            }
        }
    }
}
