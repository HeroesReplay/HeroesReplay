using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Camps;

/// <summary>
/// Counts each team's camp captures (#412). A camp's side is the core it is nearer to, by at least
/// <see cref="CampInsightSettings.SideRatio"/>; a camp in the middle belongs to neither.
/// </summary>
public sealed class CampAnalyzer : IInsightAnalyzer<CampInsights>
{
    private readonly CampInsightSettings settings;

    public CampAnalyzer(CampInsightSettings settings)
    {
        this.settings = settings ?? new CampInsightSettings();
    }

    public CampInsights Analyze(ReplaySnapshot snapshot)
    {
        if (snapshot == null || snapshot.Aram || snapshot.Cores.Count != 2)
        {
            return new CampInsights();
        }

        Dictionary<int, MapPoint> camps = snapshot
            .Camps.GroupBy(camp => camp.Id)
            .ToDictionary(group => group.Key, group => group.First().Point);
        bool longGame = snapshot.End - snapshot.GatesOpen >= settings.DroughtMinLength;
        var teams = new List<TeamCamps>();
        for (int team = 0; team < 2; team++)
        {
            List<CampCapture> captures = snapshot
                .CampCaptures.Where(capture => capture.Team == team)
                .ToList();
            int enemySide = captures.Count(capture =>
                camps.TryGetValue(capture.CampId, out MapPoint point)
                && Side(snapshot, point) == 1 - team
            );
            teams.Add(
                new TeamCamps
                {
                    Team = team,
                    Captures = captures.Count,
                    EnemySideCaptures = enemySide,
                    BossCaptures = captures.Count(capture =>
                        capture.CampType?.Contains("Boss", StringComparison.OrdinalIgnoreCase)
                        == true
                    ),
                    Drought = longGame && captures.Count <= settings.DroughtMaxCaptures,
                }
            );
        }

        return new CampInsights { Teams = teams, TotalCaptures = snapshot.CampCaptures.Count };
    }

    /// <summary>The team whose side <paramref name="point"/> is on, or -1 in the middle.</summary>
    private int Side(ReplaySnapshot snapshot, MapPoint point)
    {
        double blue = point.DistanceTo(snapshot.Cores[0]);
        double red = point.DistanceTo(snapshot.Cores[1]);
        if (blue < red * settings.SideRatio)
        {
            return 0;
        }

        return red < blue * settings.SideRatio ? 1 : -1;
    }
}
