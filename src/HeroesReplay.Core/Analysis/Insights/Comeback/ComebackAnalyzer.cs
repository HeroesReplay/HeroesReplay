using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Comeback;

/// <summary>
/// Finds an insane comeback (#412): the team whose core survived ended with fewer buildings
/// standing than the team whose core fell (by <see cref="ComebackInsightSettings.MinBuildingDeficit"/>),
/// or was <see cref="ComebackInsightSettings.MinLevelDeficit"/> levels behind at some point.
/// </summary>
public sealed class ComebackAnalyzer : IInsightAnalyzer<ComebackInsights>
{
    private readonly ComebackInsightSettings settings;

    public ComebackAnalyzer(ComebackInsightSettings settings)
    {
        this.settings = settings ?? new ComebackInsightSettings();
    }

    public ComebackInsights Analyze(ReplaySnapshot snapshot)
    {
        if (snapshot?.CoreLostBy is not int fallen || snapshot.Aram)
        {
            return new ComebackInsights();
        }

        int standing = 1 - fallen;
        int deficit = Lost(snapshot, standing) - Lost(snapshot, fallen);
        int levels = MaxLevelDeficit(snapshot, standing, fallen);
        return new ComebackInsights
        {
            BuildingDeficit = deficit,
            MaxLevelDeficit = levels,
            Comeback = deficit >= settings.MinBuildingDeficit || levels >= settings.MinLevelDeficit,
        };
    }

    private static int Lost(ReplaySnapshot snapshot, int team) =>
        snapshot.TownLosses.Count(loss => loss.Team == team)
        + snapshot.TowerLosses.Count(loss => loss.Team == team);

    private int MaxLevelDeficit(ReplaySnapshot snapshot, int standing, int fallen)
    {
        if (
            !snapshot.TeamLevels.TryGetValue(standing, out IReadOnlyDictionary<int, TimeSpan> mine)
            || !snapshot.TeamLevels.TryGetValue(
                fallen,
                out IReadOnlyDictionary<int, TimeSpan> theirs
            )
        )
        {
            return 0;
        }

        TimeSpan step =
            settings.SampleEvery > TimeSpan.Zero ? settings.SampleEvery : TimeSpan.FromSeconds(30);
        int worst = 0;
        for (TimeSpan time = snapshot.GatesOpen; time <= snapshot.End; time += step)
        {
            worst = Math.Max(worst, LevelAt(theirs, time) - LevelAt(mine, time));
        }

        return worst;
    }

    private static int LevelAt(IReadOnlyDictionary<int, TimeSpan> levels, TimeSpan time)
    {
        int level = 1;
        foreach (KeyValuePair<int, TimeSpan> reached in levels)
        {
            if (reached.Value <= time && reached.Key > level)
            {
                level = reached.Key;
            }
        }

        return level;
    }
}
