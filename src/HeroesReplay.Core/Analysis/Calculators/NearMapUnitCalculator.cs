using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;

namespace HeroesReplay.Core.Analysis.Calculators;

/// <summary>
/// A hero at a live map objective (#234). An objective unit that exists only while its objective
/// is live (payload, tribute, seed, zerg wave, immortal, warhead) is activity, and the hero near
/// it scores <see cref="WeightSettings.ObjectiveActivity"/>. A structure that stands all game
/// (<see cref="FocusUnitSettings.StructureContains"/>: watchtower, shrine, altar, cage, turn-in)
/// scores only while an enemy hero is there too. Standing near a camp scores nothing here: its
/// clear and its capture are their own calculators. Pickups keep their small weight.
/// </summary>
public class NearMapUnitCalculator : IFocusCalculator
{
    // Closeness and ownership stay small next to the step to the next weight.
    private const float ClosenessSpan = 0.15f;
    private const float ControllerBias = 0.05f;

    private readonly AppSettings settings;

    public NearMapUnitCalculator(AppSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    private enum Kind
    {
        Objective,
        Structure,
        Pickup,
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
            if (unit == null || !TryClassify(unit.Name, out Kind kind, out float baseWeight))
            {
                continue;
            }

            Dictionary<int, Point> points = Matches(focusUnits.EscortContains, unit.Name)
                ? EscortedPath.Track(unit, timeline)
                : PointsFor(unit, timeline.TotalSeconds);
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

                    if (
                        FocusActivity.IdleRemoteBody(
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

                    if (
                        kind == Kind.Structure
                        && !FocusActivity.EnemyNear(
                            timeline,
                            heroUnit,
                            heroPoint,
                            second,
                            settings.Spectate.MaxDistanceToEnemy
                        )
                    )
                    {
                        continue;
                    }

                    float closeness = (float)(1d - distance / maxDistance) * ClosenessSpan;
                    bool controller = unit.PlayerControlledBy == heroUnit.PlayerControlledBy;
                    string why = kind == Kind.Structure ? "contested" : "near";
                    timeline.Offer(
                        now,
                        GetType(),
                        heroUnit,
                        heroUnit.PlayerControlledBy,
                        baseWeight + closeness + (controller ? ControllerBias : 0f),
                        $"{heroUnit.PlayerControlledBy.Character} {why} {unit.Name} ({distance:0.0})."
                    );
                }
            }
        }
    }

    private bool TryClassify(string name, out Kind kind, out float weight)
    {
        kind = Kind.Objective;
        weight = 0f;
        if (string.IsNullOrWhiteSpace(name) || Skip(name))
        {
            return false;
        }

        FocusUnitSettings focusUnits = settings.FocusUnits;
        if (Matches(focusUnits.ObjectiveContains, name))
        {
            kind = Matches(focusUnits.StructureContains, name) ? Kind.Structure : Kind.Objective;
            weight = settings.Weights.ObjectiveActivity;
            return weight > 0f;
        }

        if (Matches(focusUnits.PickupContains, name))
        {
            kind = Kind.Pickup;
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

    /// <summary>
    /// Where the unit is each second. The replay samples a moving unit sparsely: Hanamura's payload
    /// has one position, where it was delivered. Between known points (its spawn point first) the
    /// path is interpolated, so a hero escorting it is near it the whole way.
    /// </summary>
    internal static Dictionary<int, Point> PointsFor(Unit unit, int totalSeconds)
    {
        var points = new Dictionary<int, Point>();
        int born = Math.Max(0, unit.TimeSpanBorn.FloorSeconds());
        int last = unit.TimeSpanDied.HasValue
            ? Math.Min(unit.TimeSpanDied.Value.FloorSeconds(), totalSeconds - 1)
            : totalSeconds - 1;
        var known = new SortedDictionary<int, Point>();
        if (unit.PointBorn != null)
        {
            known[born] = unit.PointBorn;
        }

        foreach (Position position in unit.Positions ?? new List<Position>())
        {
            if (position?.Point == null)
            {
                continue;
            }

            int second = position.TimeSpan.FloorSeconds();
            if ((uint)second < (uint)totalSeconds)
            {
                known[second] = position.Point;
            }
        }

        if (known.Count == 0)
        {
            return points;
        }

        KeyValuePair<int, Point>[] samples = known.ToArray();
        for (int i = 0; i < samples.Length; i++)
        {
            (int from, Point start) = (samples[i].Key, samples[i].Value);
            if (i + 1 < samples.Length)
            {
                (int to, Point end) = (samples[i + 1].Key, samples[i + 1].Value);
                for (int second = from; second < to; second++)
                {
                    double t = (double)(second - from) / (to - from);
                    points[second] = new Point
                    {
                        X = (int)Math.Round(start.X + (end.X - start.X) * t),
                        Y = (int)Math.Round(start.Y + (end.Y - start.Y) * t),
                    };
                }
            }
            else
            {
                // After the last sample the unit holds still until it dies or the game ends.
                for (int second = from; second <= last; second++)
                {
                    points[second] = start;
                }
            }
        }

        return points;
    }
}
