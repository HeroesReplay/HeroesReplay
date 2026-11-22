using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Heroes.ReplayParser;
using Heroes.ReplayParser.MPQFiles;

namespace HeroesReplay.Core.Analysis;

/// <summary>
/// Positions for two heroes the parser leaves almost without any. The parser estimates a hero's
/// position from its player's right-clicks, and skips every player whose hero units have more than
/// one name. In the production corpus a hero has a position no older than 4 s for 92 % of its
/// living seconds, but D.Va (mech and pilot) for 29 % and Gall, who never moves himself, for
/// 30 %. Those heroes were rarely "near" anything for the focus calculators, and their clips were
/// dropped as not on camera.
/// <list type="bullet">
/// <item><b>Gall</b> rides Cho: he gets Cho's positions.</item>
/// <item><b>D.Va</b> is one body at a time: her right-clicks are walked for the body alive at the
/// time, the way the parser walks every other hero's.</item>
/// </list>
/// The Lost Vikings (three bodies ordered apart) and Abathur stay as the parser left them.
/// </summary>
public static class HeroPositionFill
{
    /// <summary>The parser's own walking speed for an estimated position, in map units a second.</summary>
    private const double SpeedPerSecond = 5.0;

    private static readonly ConditionalWeakTable<Replay, object> Filled = new();

    /// <summary>Adds the positions once per replay; later calls for the same replay do nothing.</summary>
    public static void Apply(Replay replay)
    {
        if (replay?.Players == null || !Filled.TryAdd(replay, new object()))
        {
            return;
        }

        foreach (Player player in replay.Players)
        {
            List<Unit> units = player?.HeroUnits;
            if (units == null || units.Count == 0)
            {
                continue;
            }

            if (units.All(unit => unit?.Name == "HeroGall"))
            {
                FollowCho(replay, player);
            }
            else if (
                units.All(unit => unit?.Name is "HeroDVaMech" or "HeroDVaPilot")
                && units.Select(unit => unit.Name).Distinct().Count() > 1
            )
            {
                WalkOrders(replay, player);
            }
        }
    }

    private static void FollowCho(Replay replay, Player gall)
    {
        Player cho = replay.Players.FirstOrDefault(player =>
            player != null
            && player.Team == gall.Team
            && player.HeroUnits?.Any(unit => unit?.Name == "HeroCho") == true
        );
        if (cho == null)
        {
            return;
        }

        List<Position> ridden = cho
            .HeroUnits.Where(unit => unit?.Positions != null)
            .SelectMany(unit => unit.Positions)
            .Where(position => position?.Point != null)
            .ToList();
        foreach (Unit unit in gall.HeroUnits)
        {
            unit.Positions ??= new List<Position>();
            unit.Positions.AddRange(
                ridden.Where(position =>
                    position.TimeSpan > unit.TimeSpanBorn
                    && (unit.TimeSpanDied == null || position.TimeSpan < unit.TimeSpanDied.Value)
                )
            );
            unit.Positions = OnePerSecond(unit.Positions);
        }
    }

    private static void WalkOrders(Replay replay, Player player)
    {
        List<Position> orders = OnePerSecond(Orders(replay, player));
        foreach (Unit unit in player.HeroUnits)
        {
            if (unit.PointBorn == null)
            {
                continue;
            }

            unit.Positions ??= new List<Position>();
            var stops = new List<Position>(
                unit.Positions.Where(position => position?.Point != null)
            )
            {
                new Position { TimeSpan = unit.TimeSpanBorn, Point = unit.PointBorn },
            };
            stops.AddRange(
                orders.Where(order =>
                    order.TimeSpan > unit.TimeSpanBorn
                    && (unit.TimeSpanDied == null || order.TimeSpan < unit.TimeSpanDied.Value)
                )
            );
            stops = OnePerSecond(stops);
            Position current = stops[0];
            for (int index = 1; index < stops.Count; index++)
            {
                Position next = stops[index];
                if (!next.IsEstimated)
                {
                    current = next;
                    continue;
                }

                double distance = current.Point.DistanceTo(next.Point);
                double share =
                    distance <= 0
                        ? 1
                        : (next.TimeSpan - current.TimeSpan).TotalSeconds
                            * SpeedPerSecond
                            / distance;
                current = new Position
                {
                    TimeSpan = next.TimeSpan,
                    Point =
                        share >= 1
                            ? next.Point
                            : new Point
                            {
                                X = (int)(
                                    (next.Point.X - current.Point.X) * share + current.Point.X
                                ),
                                Y = (int)(
                                    (next.Point.Y - current.Point.Y) * share + current.Point.Y
                                ),
                            },
                    IsEstimated = true,
                };
                unit.Positions.Add(current);
            }

            unit.Positions = OnePerSecond(unit.Positions);
        }
    }

    /// <summary>The points the player right-clicked: a move order, or a held right mouse button.</summary>
    private static List<Position> Orders(Replay replay, Player player)
    {
        var orders = new List<Position>();
        foreach (GameEvent order in replay.GameEvents ?? new List<GameEvent>())
        {
            if (order?.player != player)
            {
                continue;
            }

            TrackerEventStructure[] point = null;
            if (order.eventType == GameEventType.CCmdEvent)
            {
                TrackerEventStructure[] data = order.data?.array;
                if (data != null && data.Length > 2 && data[1] == null)
                {
                    point = data[2]?.array;
                }

                if (point?.Length != 3)
                {
                    point = null;
                }
            }
            else if (order.eventType == GameEventType.CCmdUpdateTargetPointEvent)
            {
                point = order.data?.array;
            }

            if (
                point == null
                || point.Length < 2
                || point[0]?.unsignedInt == null
                || point[1]?.unsignedInt == null
            )
            {
                continue;
            }

            orders.Add(
                new Position
                {
                    TimeSpan = order.TimeSpan,
                    Point = Point.FromEventFormat(
                        point[0].unsignedInt.Value,
                        point[1].unsignedInt.Value
                    ),
                    IsEstimated = true,
                }
            );
        }

        return orders;
    }

    /// <summary>One position a second in time order: a measured one first, else the latest.</summary>
    private static List<Position> OnePerSecond(IEnumerable<Position> positions) =>
        positions
            .GroupBy(position => (int)position.TimeSpan.TotalSeconds)
            .Select(second =>
                second
                    .OrderBy(position => position.IsEstimated)
                    .ThenByDescending(position => position.TimeSpan)
                    .First()
            )
            .OrderBy(position => position.TimeSpan)
            .ToList();
}
