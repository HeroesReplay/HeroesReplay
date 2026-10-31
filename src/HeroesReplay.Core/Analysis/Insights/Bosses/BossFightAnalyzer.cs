using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Bosses;

/// <summary>
/// The fight at the pit before each boss capture (#528). The fight is the run of seconds up to the
/// capture in which a hero of either team was within <see cref="BossFightInsightSettings.Radius"/>
/// of the camp, with gaps of at most <see cref="BossFightInsightSettings.GapSeconds"/>. A team has a
/// group there with <see cref="BossFightInsightSettings.MinGroup"/> heroes. The replay does not
/// record who damaged the boss, so who was taking it is read from where the heroes stood.
/// </summary>
public sealed class BossFightAnalyzer : IInsightAnalyzer<BossFightInsights>
{
    private readonly BossFightInsightSettings settings;

    public BossFightAnalyzer(BossFightInsightSettings settings)
    {
        this.settings = settings ?? new BossFightInsightSettings();
    }

    public BossFightInsights Analyze(ReplaySnapshot snapshot)
    {
        if (
            snapshot?.BossCaptures == null
            || snapshot.Positions == null
            || snapshot.Aram
            || snapshot.BossCaptures.Count == 0
        )
        {
            return new BossFightInsights();
        }

        return new BossFightInsights
        {
            Fights = snapshot.BossCaptures.Select(capture => Fight(snapshot, capture)).ToArray(),
        };
    }

    private BossFight Fight(ReplaySnapshot snapshot, BossCapture capture)
    {
        int end = capture.Time.FloorSeconds();
        var counts = new List<(int Second, int Blue, int Red)>();
        int gap = 0;
        for (int second = end; second >= Math.Max(0, end - settings.MaxFightSeconds); second--)
        {
            (int blue, int red) = AtPit(snapshot, capture.Point, second);
            if (blue + red > 0)
            {
                // The empty seconds after a hero was seen belong to the fight.
                for (int skipped = gap; skipped > 0; skipped--)
                {
                    counts.Add((second + skipped, 0, 0));
                }

                counts.Add((second, blue, red));
                gap = 0;
            }
            else if (++gap > settings.GapSeconds)
            {
                break;
            }
        }

        counts.Reverse();
        int start = counts.Count > 0 ? counts[0].Second : end;
        int minGroup = Math.Max(1, settings.MinGroup);
        BossFightTeam[] teams = new[] { 0, 1 }
            .Select(team =>
            {
                int Heroes((int Second, int Blue, int Red) row) => team == 0 ? row.Blue : row.Red;
                return new BossFightTeam
                {
                    Team = team,
                    HeroSeconds = counts.Sum(Heroes),
                    GroupSeconds = counts.Count(row => Heroes(row) >= minGroup),
                    MostHeroes = counts.Select(Heroes).DefaultIfEmpty().Max(),
                    FirstHeroSecond = counts
                        .Where(row => Heroes(row) > 0)
                        .Select(row => (int?)row.Second)
                        .FirstOrDefault(),
                    FirstGroupSecond = counts
                        .Where(row => Heroes(row) >= minGroup)
                        .Select(row => (int?)row.Second)
                        .FirstOrDefault(),
                    Deaths = snapshot.HeroDeaths.Count(death =>
                        death.Team == team
                        && death.Time.FloorSeconds() >= start
                        && death.Time.FloorSeconds() <= end + settings.DeathGraceSeconds
                        && death.Point.DistanceTo(capture.Point) <= settings.DeathRadius
                    ),
                };
            })
            .ToArray();
        int together = counts.Count(row => row.Blue >= minGroup && row.Red >= minGroup);
        int? starter = Starter(teams);
        SnapshotPlayer killer = snapshot.Players.FirstOrDefault(player =>
            player.Index == capture.KillerIndex
        );
        return new BossFight
        {
            CampId = capture.CampId,
            Point = capture.Point,
            Team = capture.Team,
            Second = end,
            StartSecond = start,
            Starter = starter,
            Side = Side(snapshot, capture.Point),
            KillerIndex = capture.KillerIndex,
            KillerHero = killer?.HeroName,
            KillerTeam = killer?.Team,
            Teams = teams,
            TogetherSeconds = together,
            Kind =
                killer != null && killer.Team != capture.Team
                    ? KilledByOtherTeam(capture.Team, starter, teams)
                    : Kind(capture.Team, starter, teams, together),
        };
    }

    /// <summary>
    /// The other team landed the killing blow and the capturing team took the circle. When the
    /// capturing team clearly started the boss, the other team's steal failed: the boss was
    /// defended. Otherwise the capture stole it.
    /// </summary>
    private BossFightKind KilledByOtherTeam(int capturer, int? starter, BossFightTeam[] teams) =>
        starter == capturer && Lead(teams, capturer) >= settings.MinLeadSeconds
            ? BossFightKind.Defended
            : BossFightKind.Steal;

    /// <summary>How long <paramref name="team"/>'s group was at the pit before the other team's first hero.</summary>
    private static long Lead(BossFightTeam[] teams, int team) =>
        (teams[1 - team].FirstHeroSecond ?? int.MaxValue)
        - (long)(teams[team].FirstGroupSecond ?? int.MaxValue);

    private (int Blue, int Red) AtPit(ReplaySnapshot snapshot, MapPoint pit, int second)
    {
        TimeSpan time = TimeSpan.FromSeconds(second);
        int blue = 0;
        int red = 0;
        foreach (SnapshotPlayer player in snapshot.Players)
        {
            if (
                !snapshot.Positions.TryGet(player.Index, time, out MapPoint point)
                || point.DistanceTo(pit) > settings.Radius
            )
            {
                continue;
            }

            if (player.Team == 0)
            {
                blue++;
            }
            else if (player.Team == 1)
            {
                red++;
            }
        }

        return (blue, red);
    }

    /// <summary>The team whose group reached the pit first; the other team when only it grouped.</summary>
    private static int? Starter(BossFightTeam[] teams)
    {
        int? blue = teams[0].FirstGroupSecond;
        int? red = teams[1].FirstGroupSecond;
        if (blue == null && red == null)
        {
            return null;
        }

        if (red == null || blue < red)
        {
            return 0;
        }

        if (blue == null || red < blue)
        {
            return 1;
        }

        return null;
    }

    private BossFightKind Kind(int capturer, int? starter, BossFightTeam[] teams, int together)
    {
        if (starter == null)
        {
            return BossFightKind.Take;
        }

        // The team that started it, clearly: its group was there before any hero of the other team.
        BossFightTeam taking = teams[starter.Value];
        if (Lead(teams, starter.Value) < settings.MinLeadSeconds)
        {
            return BossFightKind.Take;
        }

        if (starter != capturer)
        {
            return taking.GroupSeconds >= settings.StealMinGroupSeconds
                ? BossFightKind.Steal
                : BossFightKind.Take;
        }

        return
            together >= settings.ContestMinSeconds
            && teams.Sum(team => team.Deaths) >= settings.ContestMinDeaths
            ? BossFightKind.Defended
            : BossFightKind.Take;
    }

    /// <summary>The team whose core <paramref name="point"/> is nearer by <see cref="BossFightInsightSettings.SideRatio"/>, or -1.</summary>
    private int Side(ReplaySnapshot snapshot, MapPoint point)
    {
        if (
            !snapshot.Cores.TryGetValue(0, out MapPoint blueCore)
            || !snapshot.Cores.TryGetValue(1, out MapPoint redCore)
        )
        {
            return -1;
        }

        double blue = point.DistanceTo(blueCore);
        double red = point.DistanceTo(redCore);
        if (blue < red * settings.SideRatio)
        {
            return 0;
        }

        return red < blue * settings.SideRatio ? 1 : -1;
    }
}
