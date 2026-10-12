using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.Analysis.Insights.Structures;

/// <summary>Counts the forts and keeps each team lost (#412), from <c>TownStructureDeath</c>.</summary>
public sealed class StructureAnalyzer : IInsightAnalyzer<StructureInsights>
{
    public StructureInsights Analyze(ReplaySnapshot snapshot)
    {
        var teams = new List<TeamStructures>();
        for (int team = 0; team < 2; team++)
        {
            List<TownLoss> losses = (snapshot?.TownLosses ?? new List<TownLoss>())
                .Where(loss => loss.Team == team)
                .ToList();
            teams.Add(
                new TeamStructures
                {
                    Team = team,
                    FortsLost = losses.Count(loss => !loss.Keep),
                    KeepsLost = losses.Count(loss => loss.Keep),
                    LastLoss = losses.Count == 0 ? null : losses.Max(loss => loss.Time),
                }
            );
        }

        return new StructureInsights { Teams = teams };
    }
}
