using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Tests.Unit.Analysis.Insights;

/// <summary>
/// A three-lane map for insight tests: Blue's core at (40, 110), Red's at (210, 110), lane 1 at
/// y 160, lane 2 at y 110 and lane 3 at y 60, each with a Blue town at x 90 and a Red town at x 160.
/// </summary>
internal static class InsightFixtures
{
    public static readonly MapPoint BlueCore = new(40, 110);
    public static readonly MapPoint RedCore = new(210, 110);

    public static readonly IReadOnlyList<Town> Towns = new[] { (1, 160), (2, 110), (3, 60) }
        .SelectMany(lane =>
            new[]
            {
                new Town(lane.Item1, 0, lane.Item1, new MapPoint(90, lane.Item2)),
                new Town(lane.Item1 + 10, 1, lane.Item1, new MapPoint(160, lane.Item2)),
            }
        )
        .ToArray();

    public static SnapshotPlayer Player(
        int index,
        int team,
        string hero,
        PlayerScore score = null,
        bool ai = false
    ) =>
        new()
        {
            Index = index,
            Team = team,
            Hero =
                hero == null
                    ? null
                    : new Hero(hero, "Hero" + hero.Replace(" ", string.Empty), null, null),
            IsAi = ai,
            Score = score ?? new PlayerScore(),
        };

    /// <summary>A twenty-minute match whose teams reach level 10 six minutes after gates open.</summary>
    public static ReplaySnapshot Snapshot(
        IReadOnlyList<SnapshotPlayer> players,
        IHeroPositions positions = null
    ) =>
        new()
        {
            GatesOpen = TimeSpan.Zero,
            End = TimeSpan.FromMinutes(20),
            Players = players,
            Cores = new Dictionary<int, MapPoint> { [0] = BlueCore, [1] = RedCore },
            Towns = Towns,
            TeamLevels = new Dictionary<int, IReadOnlyDictionary<int, TimeSpan>>
            {
                [0] = new Dictionary<int, TimeSpan> { [10] = TimeSpan.FromMinutes(6) },
                [1] = new Dictionary<int, TimeSpan> { [10] = TimeSpan.FromMinutes(6) },
            },
            Positions = positions,
        };
}

/// <summary>Positions from a fixed point per player, or a function of time.</summary>
internal sealed class ScriptedPositions : IHeroPositions
{
    private readonly Dictionary<int, Func<TimeSpan, MapPoint?>> paths = new();

    public ScriptedPositions At(int playerIndex, MapPoint point) => Path(playerIndex, _ => point);

    public ScriptedPositions Path(int playerIndex, Func<TimeSpan, MapPoint?> path)
    {
        paths[playerIndex] = path;
        return this;
    }

    public bool TryGet(int playerIndex, TimeSpan time, out MapPoint point)
    {
        point = default;
        if (
            !paths.TryGetValue(playerIndex, out Func<TimeSpan, MapPoint?> path)
            || path(time) is not MapPoint at
        )
        {
            return false;
        }

        point = at;
        return true;
    }
}
