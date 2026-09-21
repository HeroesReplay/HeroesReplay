using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;
using HeroesReplay.Core.Services.Data;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

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

            var points = new Dictionary<int, Point>();
            if (unit.Positions != null)
            {
                foreach (Position position in unit.Positions)
                {
                    points[position.TimeSpan.FloorSeconds()] = position.Point;
                }
            }

            bosses.Add((unit, points));
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

                    if (heroPoint.DistanceTo(bossPoint) >= settings.Spectate.MaxDistanceToBoss)
                    {
                        continue;
                    }

                    timeline.Offer(
                        now,
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        settings.Weights.BossCapture,
                        $"{heroUnit.PlayerControlledBy.Character} near {boss.Name} (Boss)"
                    );
                }
            }
        }
    }
}
