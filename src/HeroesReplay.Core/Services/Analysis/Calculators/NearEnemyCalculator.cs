using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class NearEnemyCalculator : IFocusCalculator
{
    private readonly AppSettings settings;

    public NearEnemyCalculator(AppSettings settings)
    {
        this.settings = settings;
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        Player[] players = timeline.Replay.Players ?? Array.Empty<Player>();
        var teamZero = new List<Unit>(5);
        var teamOne = new List<Unit>(5);

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            teamZero.Clear();
            teamOne.Clear();

            foreach (Unit unit in timeline.AliveHeroesAt(second))
            {
                if (unit.Team == 0)
                {
                    teamZero.Add(unit);
                }
                else if (unit.Team == 1)
                {
                    teamOne.Add(unit);
                }
            }

            if (teamZero.Count == 0 || teamOne.Count == 0)
            {
                continue;
            }

            TimeSpan now = TimeSpan.FromSeconds(second);

            foreach (Unit teamOneUnit in teamZero)
            {
                if (!timeline.TryGetPoint(teamOneUnit, second, out Point teamOnePoint))
                {
                    continue;
                }

                foreach (Unit teamTwoUnit in teamOne)
                {
                    if (!timeline.TryGetPoint(teamTwoUnit, second, out Point teamTwoPoint))
                    {
                        continue;
                    }

                    double distance = teamTwoPoint.DistanceTo(teamOnePoint);
                    if (distance > settings.Spectate.MaxDistanceToEnemy)
                    {
                        continue;
                    }

                    Unit target = PreferStableHero(teamOneUnit, teamTwoUnit, players);
                    Unit enemy = target == teamOneUnit ? teamTwoUnit : teamOneUnit;
                    if (target.PlayerControlledBy == null || enemy.PlayerControlledBy == null)
                    {
                        continue;
                    }

                    float closer =
                        Convert.ToSingle(distance) / settings.Weights.NearEnemyHeroDistanceDivisor;
                    timeline.Offer(
                        now,
                        GetType(),
                        target,
                        target.PlayerControlledBy,
                        settings.Weights.NearEnemyHero
                            + settings.Weights.NearEnemyHeroOffset
                            - closer,
                        $"{target.PlayerControlledBy.Character} is in proximity of {enemy.PlayerControlledBy.Character} ({distance})"
                    );
                }
            }
        }
    }

    private static Unit PreferStableHero(Unit left, Unit right, Player[] players)
    {
        int leftIndex = Array.IndexOf(players, left.PlayerControlledBy);
        int rightIndex = Array.IndexOf(players, right.PlayerControlledBy);
        if (leftIndex < 0)
        {
            leftIndex = int.MaxValue;
        }

        if (rightIndex < 0)
        {
            rightIndex = int.MaxValue;
        }

        return leftIndex <= rightIndex ? left : right;
    }
}
