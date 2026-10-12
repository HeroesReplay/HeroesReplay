using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Lanes;

/// <summary>
/// Finds each team's solo laner from hero positions while laning lasts (#412): from gates open
/// plus <see cref="LaneInsightSettings.WindowStart"/> to the first team's level 10. Every sample,
/// each living player away from their base gets the lane of the nearest fort or keep. The group
/// lane is where the three least isolated players spend the window; a soaker spends most of it
/// alone in another lane. Measured on 202 teams: one soaker for 59%, mirrored by the other team's
/// soaker in the same lane for 38% (High), and the same hero as in the first five minutes 92% of
/// the time.
/// </summary>
public sealed class LaneAnalyzer : IInsightAnalyzer<LaneInsights>
{
    private const int GroupSize = 3;
    private const int MinJudged = 4;
    private const int TopMinionRank = 2;
    private const int LevelTen = 10;

    private readonly LaneInsightSettings settings;

    public LaneAnalyzer(LaneInsightSettings settings)
    {
        this.settings = settings ?? new LaneInsightSettings();
    }

    public LaneInsights Analyze(ReplaySnapshot snapshot)
    {
        if (
            snapshot == null
            || snapshot.Aram
            || snapshot.Towns.Count == 0
            || snapshot.Cores.Count != 2
            || snapshot.Positions == null
        )
        {
            return new LaneInsights();
        }

        TimeSpan start = snapshot.GatesOpen + settings.WindowStart;
        TimeSpan end = WindowEnd(snapshot);
        var empty = new LaneInsights
        {
            WindowStart = start,
            WindowEnd = end,
            LaneCount = snapshot.Towns.Select(town => town.Lane).Distinct().Count(),
        };
        if (end - start < TimeSpan.FromMinutes(1))
        {
            return empty;
        }

        var judged = new Dictionary<int, (int GroupLane, List<LaneSoaker> Soakers)>();
        for (int team = 0; team < 2; team++)
        {
            judged[team] = Judge(snapshot, team, start, end);
        }

        var teams = new List<TeamLanes>();
        for (int team = 0; team < 2; team++)
        {
            List<LaneSoaker> enemy = judged[1 - team].Soakers;
            List<LaneSoaker> soakers = judged[team]
                .Soakers.Select(soaker =>
                    soaker with
                    {
                        Mirrored = enemy.Any(other => other.Lane == soaker.Lane),
                    }
                )
                .ToList();
            teams.Add(Verdict(team, judged[team].GroupLane, soakers));
        }

        return empty with
        {
            Teams = teams,
        };
    }

    private TimeSpan WindowEnd(ReplaySnapshot snapshot)
    {
        TimeSpan end = snapshot.GatesOpen + settings.WindowCap;
        foreach (IReadOnlyDictionary<int, TimeSpan> levels in snapshot.TeamLevels.Values)
        {
            if (levels.TryGetValue(LevelTen, out TimeSpan reached) && reached < end)
            {
                end = reached;
            }
        }

        return end < snapshot.End ? end : snapshot.End;
    }

    private (int GroupLane, List<LaneSoaker> Soakers) Judge(
        ReplaySnapshot snapshot,
        int team,
        TimeSpan start,
        TimeSpan end
    )
    {
        List<SnapshotPlayer> mates = snapshot.Players.Where(player => player.Team == team).ToList();
        MapPoint home = snapshot.Cores[team];
        var lanes = mates.ToDictionary(player => player.Index, _ => new Dictionary<int, int>());
        var alone = mates.ToDictionary(player => player.Index, _ => (Alone: 0, Samples: 0));
        TimeSpan step =
            settings.SampleEvery > TimeSpan.Zero ? settings.SampleEvery : TimeSpan.FromSeconds(2);

        for (TimeSpan time = start; time < end; time += step)
        {
            var points = new Dictionary<int, MapPoint>();
            foreach (SnapshotPlayer mate in mates)
            {
                if (
                    snapshot.Positions.TryGet(mate.Index, time, out MapPoint point)
                    && point.DistanceTo(home) > settings.BaseRadius
                )
                {
                    points[mate.Index] = point;
                }
            }

            foreach (KeyValuePair<int, MapPoint> player in points)
            {
                int lane = NearestLane(snapshot, player.Value);
                lanes[player.Key][lane] = lanes[player.Key].GetValueOrDefault(lane) + 1;
                if (points.Count >= GroupSize)
                {
                    bool isAlone = points
                        .Where(other => other.Key != player.Key)
                        .All(other => other.Value.DistanceTo(player.Value) > settings.AloneRadius);
                    alone[player.Key] = (
                        alone[player.Key].Alone + (isAlone ? 1 : 0),
                        alone[player.Key].Samples + 1
                    );
                }
            }
        }

        var rows = mates
            .Where(player => lanes[player.Index].Values.Sum() >= settings.MinSamples)
            .Select(player =>
            {
                int total = lanes[player.Index].Values.Sum();
                KeyValuePair<int, int> top = lanes[player.Index]
                    .OrderByDescending(lane => lane.Value)
                    .ThenBy(lane => lane.Key)
                    .First();
                (int isAlone, int samples) = alone[player.Index];
                return (
                    Player: player,
                    Lane: top.Key,
                    Share: top.Value / (double)total,
                    Alone: samples == 0 ? 0 : isAlone / (double)samples
                );
            })
            .ToList();
        if (rows.Count < MinJudged)
        {
            return (0, new List<LaneSoaker>());
        }

        var grouped = new Dictionary<int, int>();
        foreach (var row in rows.OrderBy(row => row.Alone).Take(GroupSize))
        {
            foreach (KeyValuePair<int, int> lane in lanes[row.Player.Index])
            {
                grouped[lane.Key] = grouped.GetValueOrDefault(lane.Key) + lane.Value;
            }
        }

        int groupLane = grouped
            .OrderByDescending(lane => lane.Value)
            .ThenBy(lane => lane.Key)
            .First()
            .Key;
        HashSet<int> topMinion = mates
            .OrderByDescending(player => player.Score.MinionDamage)
            .Take(TopMinionRank)
            .Select(player => player.Index)
            .ToHashSet();
        List<LaneSoaker> soakers = rows.Where(row =>
                row.Lane != groupLane
                && row.Share >= settings.SoakerLaneShare
                && row.Alone >= settings.SoakerAlone
                && !Excluded(row.Player)
            )
            .OrderByDescending(row => row.Alone)
            .Select(row => new LaneSoaker
            {
                PlayerIndex = row.Player.Index,
                Hero = row.Player.HeroName,
                Lane = row.Lane,
                LaneShare = Math.Round(row.Share, 3),
                AloneShare = Math.Round(row.Alone, 3),
                TopMinionDamage = topMinion.Contains(row.Player.Index),
            })
            .ToList();
        return (groupLane, soakers);
    }

    private static TeamLanes Verdict(int team, int groupLane, List<LaneSoaker> soakers)
    {
        var lanes = new TeamLanes
        {
            Team = team,
            GroupLane = groupLane,
            Soakers = soakers,
        };
        if (soakers.Count == 0)
        {
            return lanes with { Confidence = LaneConfidence.None };
        }

        if (soakers.Count > 1)
        {
            return lanes with
            {
                SoloLaner = soakers.FirstOrDefault(soaker => soaker.Mirrored) ?? soakers[0],
                Confidence = LaneConfidence.Low,
            };
        }

        LaneSoaker solo = soakers[0];
        LaneConfidence confidence =
            solo.Mirrored ? LaneConfidence.High
            : solo.TopMinionDamage ? LaneConfidence.Medium
            : LaneConfidence.Low;
        return lanes with { SoloLaner = solo, Confidence = confidence };
    }

    private static int NearestLane(ReplaySnapshot snapshot, MapPoint point)
    {
        Town nearest = null;
        double best = double.MaxValue;
        foreach (Town town in snapshot.Towns)
        {
            double distance = town.Point.DistanceTo(point);
            if (distance < best)
            {
                best = distance;
                nearest = town;
            }
        }

        return nearest?.Lane ?? 0;
    }

    private bool Excluded(SnapshotPlayer player) =>
        player.HeroName != null
        && (settings.ExcludedHeroes ?? Array.Empty<string>()).Contains(
            player.HeroName,
            StringComparer.OrdinalIgnoreCase
        );
}
