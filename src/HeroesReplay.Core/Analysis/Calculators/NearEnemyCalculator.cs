using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Analysis.Calculators;

/// <summary>
/// Fights between heroes (#234). Each second the alive heroes are grouped: two heroes within
/// <c>Spectate:MaxDistanceToEnemy</c> of each other are in the same group, and a group with
/// heroes of both teams is a fight. A bigger fight scores higher (<see
/// cref="WeightSettings.TeamfightPerHero"/> for each hero beyond the first two, up to <see
/// cref="WeightSettings.TeamfightMax"/>, below a death or a kill), so a 5v5 wins over a 1v1
/// elsewhere. The camera takes the hero nearest the middle of the fight, and stays on the hero it
/// had while that hero is still in it.
/// </summary>
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
        int maxDistance = settings.Spectate.MaxDistanceToEnemy;
        WeightSettings weights = settings.Weights;
        Unit previous = null;
        var heroes = new List<(Unit Unit, Point Point)>(10);

        for (int second = 0; second < timeline.TotalSeconds; second++)
        {
            heroes.Clear();
            foreach (Unit unit in timeline.AliveHeroesAt(second))
            {
                if (
                    (unit.Team == 0 || unit.Team == 1)
                    && unit.PlayerControlledBy != null
                    && timeline.TryGetPoint(unit, second, out Point point)
                )
                {
                    heroes.Add((unit, point));
                }
            }

            Unit kept = null;
            int keptSize = 0;
            foreach (List<int> group in Groups(heroes, maxDistance))
            {
                int blue = group.Count(i => heroes[i].Unit.Team == 0);
                int red = group.Count - blue;
                if (blue == 0 || red == 0)
                {
                    continue;
                }

                double closest = double.MaxValue;
                foreach (int i in group)
                {
                    foreach (int j in group)
                    {
                        if (heroes[i].Unit.Team != heroes[j].Unit.Team)
                        {
                            closest = Math.Min(
                                closest,
                                heroes[i].Point.DistanceTo(heroes[j].Point)
                            );
                        }
                    }
                }

                int target = Target(group, heroes, previous, players, maxDistance);
                Unit hero = heroes[target].Unit;
                float size = weights.TeamfightPerHero * (group.Count - 2);
                float points =
                    weights.NearEnemyHero
                    + weights.NearEnemyHeroOffset
                    + size
                    - Convert.ToSingle(closest) / weights.NearEnemyHeroDistanceDivisor;
                if (weights.TeamfightMax > 0f)
                {
                    points = Math.Min(points, weights.TeamfightMax);
                }

                string fight =
                    group.Count == 2
                        ? $"is in proximity of {heroes[group.First(i => i != target)].Unit.PlayerControlledBy.Character}"
                        : $"is in a {(hero.Team == 0 ? blue : red)}v{(hero.Team == 0 ? red : blue)} fight";
                timeline.Offer(
                    TimeSpan.FromSeconds(second),
                    GetType(),
                    hero,
                    hero.PlayerControlledBy,
                    points,
                    $"{hero.PlayerControlledBy.Character} {fight} ({closest:0.0})"
                );

                if (group.Count > keptSize)
                {
                    kept = hero;
                    keptSize = group.Count;
                }
            }

            previous = kept;
        }
    }

    /// <summary>Heroes linked by a chain of pairs within <paramref name="maxDistance"/>.</summary>
    internal static List<List<int>> Groups(List<(Unit Unit, Point Point)> heroes, int maxDistance)
    {
        int[] parent = Enumerable.Range(0, heroes.Count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        for (int i = 0; i < heroes.Count; i++)
        {
            for (int j = i + 1; j < heroes.Count; j++)
            {
                if (heroes[i].Point.DistanceTo(heroes[j].Point) <= maxDistance)
                {
                    parent[Find(i)] = Find(j);
                }
            }
        }

        return Enumerable
            .Range(0, heroes.Count)
            .GroupBy(Find)
            .Select(group => group.ToList())
            .Where(group => group.Count > 1)
            .ToList();
    }

    /// <summary>
    /// Only a hero with an enemy hero in range is engaged: a group links allies standing near each
    /// other, so its middle can be a backliner out of the fight. The camera keeps the engaged hero
    /// it had, else takes the engaged hero nearest the middle of the engaged heroes. Equal
    /// distances go to the lower player slot, so the choice does not flicker.
    /// </summary>
    private static int Target(
        List<int> group,
        List<(Unit Unit, Point Point)> heroes,
        Unit previous,
        Player[] players,
        int maxDistance
    )
    {
        List<int> engaged = group
            .Where(i =>
                group.Any(j =>
                    heroes[j].Unit.Team != heroes[i].Unit.Team
                    && heroes[j].Point.DistanceTo(heroes[i].Point) <= maxDistance
                )
            )
            .ToList();
        if (engaged.Count == 0)
        {
            engaged = group;
        }

        foreach (int i in engaged)
        {
            if (previous != null && heroes[i].Unit == previous)
            {
                return i;
            }
        }

        double x = engaged.Average(i => heroes[i].Point.X);
        double y = engaged.Average(i => heroes[i].Point.Y);
        return engaged
            .OrderBy(i => Math.Round(Distance(heroes[i].Point, x, y), 3))
            .ThenBy(i => Slot(heroes[i].Unit, players))
            .First();
    }

    private static double Distance(Point point, double x, double y) =>
        Math.Sqrt((point.X - x) * (point.X - x) + (point.Y - y) * (point.Y - y));

    private static int Slot(Unit unit, Player[] players)
    {
        int index = Array.IndexOf(players, unit.PlayerControlledBy);
        return index < 0 ? int.MaxValue : index;
    }
}
