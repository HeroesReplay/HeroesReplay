using System;
using System.Collections.Generic;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Extensions;

namespace HeroesReplay.Core.Services.Analysis.Calculators;

public class NearMapUnitCalculator : IFocusCalculator
{
    // Closeness stays inside the gap from MapObjective (9.25) to PlayerDeath (9.50).
    private const float ClosenessSpan = 0.15f;
    private const float ControllerBias = 0.05f;

    private readonly AppSettings settings;

    public NearMapUnitCalculator(AppSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public void Contribute(ReplayTimeline timeline)
    {
        if (timeline == null)
        {
            throw new ArgumentNullException(nameof(timeline));
        }

        FocusUnitSettings focusUnits = settings.FocusUnits;
        if (focusUnits == null || settings.Weights == null || settings.Spectate == null)
        {
            return;
        }

        int maxDistance = settings.Spectate.MaxDistanceToObjective;
        if (maxDistance <= 0)
        {
            return;
        }

        foreach (Unit unit in timeline.Replay.Units ?? new List<Unit>())
        {
            if (unit == null || !TryWeight(unit.Name, out float baseWeight))
            {
                continue;
            }

            Dictionary<int, Point> points = PointsFor(unit, timeline.TotalSeconds);
            if (points.Count == 0)
            {
                continue;
            }

            for (int second = 0; second < timeline.TotalSeconds; second++)
            {
                TimeSpan now = TimeSpan.FromSeconds(second);
                if (!unit.IsAliveAt(now) || !points.TryGetValue(second, out Point unitPoint))
                {
                    continue;
                }

                foreach (Unit heroUnit in timeline.AliveHeroesAt(second))
                {
                    if (
                        !timeline.TryGetPoint(heroUnit, second, out Point heroPoint)
                        || heroUnit.PlayerControlledBy == null
                    )
                    {
                        continue;
                    }

                    double distance = heroPoint.DistanceTo(unitPoint);
                    if (distance >= maxDistance)
                    {
                        continue;
                    }

                    float closeness = (float)(1d - distance / maxDistance) * ClosenessSpan;
                    bool controller = unit.PlayerControlledBy == heroUnit.PlayerControlledBy;
                    timeline.Offer(
                        now,
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        baseWeight + closeness + (controller ? ControllerBias : 0f),
                        $"{heroUnit.PlayerControlledBy.Character} near {unit.Name} ({distance:0.0})."
                    );
                }
            }
        }
    }

    private bool TryWeight(string name, out float weight)
    {
        weight = 0f;
        if (string.IsNullOrWhiteSpace(name) || Skip(name))
        {
            return false;
        }

        FocusUnitSettings focusUnits = settings.FocusUnits;
        if (Matches(focusUnits.ObjectiveContains, name))
        {
            weight = settings.Weights.MapObjective;
            return weight > 0f;
        }

        if (Matches(focusUnits.CampContains, name))
        {
            weight = settings.Weights.CampClear;
            return weight > 0f;
        }

        if (Matches(focusUnits.PickupContains, name))
        {
            weight = settings.Weights.Pickup;
            return weight > 0f;
        }

        return false;
    }

    private static bool Skip(string name)
    {
        if (name.StartsWith("Hero", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.Contains("Warning", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Preview", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dummy", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Target", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Icon", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Matches(IEnumerable<string> tokens, string name)
    {
        if (tokens == null)
        {
            return false;
        }

        foreach (string token in tokens)
        {
            if (
                !string.IsNullOrWhiteSpace(token)
                && name.Contains(token, StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<int, Point> PointsFor(Unit unit, int totalSeconds)
    {
        var points = new Dictionary<int, Point>();
        if (unit.Positions != null && unit.Positions.Count > 0)
        {
            foreach (Position position in unit.Positions)
            {
                if (position?.Point == null)
                {
                    continue;
                }

                int second = position.TimeSpan.FloorSeconds();
                if ((uint)second < (uint)totalSeconds)
                {
                    points[second] = position.Point;
                }
            }

            return points;
        }

        if (unit.PointBorn == null)
        {
            return points;
        }

        int born = unit.TimeSpanBorn.FloorSeconds();
        int died = unit.TimeSpanDied.HasValue
            ? unit.TimeSpanDied.Value.FloorSeconds()
            : totalSeconds - 1;
        for (int second = born; second <= died && second < totalSeconds; second++)
        {
            points[second] = unit.PointBorn;
        }

        return points;
    }
}
