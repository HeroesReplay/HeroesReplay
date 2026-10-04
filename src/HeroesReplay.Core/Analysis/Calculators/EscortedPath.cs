using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;

namespace HeroesReplay.Core.Analysis.Calculators;

/// <summary>
/// Where an escorted objective (Hanamura's payload) is each second (#234). The replay records the
/// payload once: where it was delivered. It moves only while a team stands on it, so each second it
/// follows the owning team's heroes near it (any heroes while it is contested), no faster than
/// <see cref="MaxSpeed"/>, and the whole path is then corrected to end at the recorded delivery.
/// </summary>
public static class EscortedPath
{
    /// <summary>Heroes this close to the payload are escorting or contesting it.</summary>
    public const double EscortRadius = 18;

    /// <summary>Map units per second; a payload never moves faster.</summary>
    public const double MaxSpeed = 2.0;

    public static Dictionary<int, Point> Track(Unit unit, ReplayTimeline timeline)
    {
        var points = new Dictionary<int, Point>();
        if (unit?.PointBorn == null || timeline == null)
        {
            return points;
        }

        int last = timeline.TotalSeconds - 1;
        int born = Math.Clamp(unit.TimeSpanBorn.FloorSeconds(), 0, last);
        int died = unit.TimeSpanDied.HasValue
            ? Math.Clamp(unit.TimeSpanDied.Value.FloorSeconds(), born, last)
            : last;
        Position delivered = unit.Positions?.LastOrDefault(position => position?.Point != null);
        int end =
            delivered == null ? died : Math.Clamp(delivered.TimeSpan.FloorSeconds(), born, died);
        List<OwnerChangeEvent> owners = (unit.OwnerChangeEvents ?? new List<OwnerChangeEvent>())
            .OrderBy(change => change.TimeSpanOwnerChanged)
            .ToList();

        double x = unit.PointBorn.X;
        double y = unit.PointBorn.Y;
        var path = new Dictionary<int, (double X, double Y)>();
        for (int second = born; second <= end; second++)
        {
            path[second] = (x, y);
            int? team = owners
                .LastOrDefault(change => change.TimeSpanOwnerChanged.FloorSeconds() <= second)
                ?.Team;
            double sumX = 0;
            double sumY = 0;
            int count = 0;
            foreach (Unit hero in timeline.AliveHeroesAt(second))
            {
                if (
                    (team == null || hero.Team == team)
                    && timeline.TryGetPoint(hero, second, out Point at)
                    && Math.Sqrt((at.X - x) * (at.X - x) + (at.Y - y) * (at.Y - y)) <= EscortRadius
                )
                {
                    sumX += at.X;
                    sumY += at.Y;
                    count++;
                }
            }

            if (count == 0)
            {
                continue;
            }

            double dx = sumX / count - x;
            double dy = sumY / count - y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance > MaxSpeed)
            {
                dx *= MaxSpeed / distance;
                dy *= MaxSpeed / distance;
            }

            x += dx;
            y += dy;
        }

        // Anchor the far end: spread the miss to the recorded delivery over the whole window.
        double missX = delivered == null ? 0 : delivered.Point.X - path[end].X;
        double missY = delivered == null ? 0 : delivered.Point.Y - path[end].Y;
        int span = Math.Max(1, end - born);
        foreach ((int second, (double px, double py)) in path)
        {
            double share = (double)(second - born) / span;
            points[second] = new Point
            {
                X = (int)Math.Round(px + missX * share),
                Y = (int)Math.Round(py + missY * share),
            };
        }

        // Delivered, it stays where it arrived until the unit is gone.
        for (int second = end + 1; second <= died; second++)
        {
            points[second] = points[end];
        }

        return points;
    }
}
