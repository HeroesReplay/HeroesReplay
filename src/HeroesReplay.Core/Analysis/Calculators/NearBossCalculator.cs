using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesData;

namespace HeroesReplay.Core.Analysis.Calculators;

public class NearBossCalculator : IFocusCalculator
{
    private readonly AppSettings settings;
    private readonly IGameData gameData;

    public NearBossCalculator(AppSettings settings, IGameData gameData)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.gameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        var bosses = new List<(Unit Unit, Dictionary<int, Point> Points)>();
        foreach (Unit unit in timeline.Replay.Units)
        {
            if (
                gameData.GetUnitGroup(unit.Name) != Unit.UnitGroup.MercenaryCamp
                || !gameData.BossUnits.Contains(unit.Name)
            )
            {
                continue;
            }

            // A laning boss is sampled every few seconds; between samples its path is interpolated.
            bosses.Add((unit, NearMapUnitCalculator.PointsFor(unit, timeline.TotalSeconds)));
        }

        if (bosses.Count == 0)
        {
            return;
        }

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            TimeSpan now = TimeSpan.FromSeconds(second);
            foreach (Unit heroUnit in timeline.AliveHeroesAt(second))
            {
                if (
                    !timeline.TryGetPoint(heroUnit, second, out Point heroPoint)
                    || heroUnit.PlayerControlledBy == null
                )
                {
                    continue;
                }

                foreach ((Unit boss, Dictionary<int, Point> bossPoints) in bosses)
                {
                    if (
                        !boss.IsAliveAt(now) || !bossPoints.TryGetValue(second, out Point bossPoint)
                    )
                    {
                        continue;
                    }

                    if (
                        heroPoint.DistanceTo(bossPoint) >= settings.Spectate.MaxDistanceToBoss
                        || FocusActivity.IdleRemoteBody(
                            timeline,
                            heroUnit,
                            heroPoint,
                            second,
                            settings.Spectate
                        )
                    )
                    {
                        continue;
                    }

                    timeline.Offer(
                        now,
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        // A live boss is an objective, not the capture itself (#234): the capture
                        // keeps BossCapture, and a fight at the boss scores as a fight.
                        settings.Weights.ObjectiveActivity,
                        $"{heroUnit.PlayerControlledBy.Character} near {boss.Name} (Boss)"
                    );
                }
            }
        }
    }
}
