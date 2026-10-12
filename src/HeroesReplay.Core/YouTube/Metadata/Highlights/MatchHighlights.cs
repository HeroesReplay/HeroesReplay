using System.Collections.Generic;
using HeroesReplay.Core.Analysis.Insights.Camps;
using HeroesReplay.Core.Analysis.Insights.Snapshot;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// Highlights about the match's shape (#412): a core race, an insane comeback, and a team that
/// took almost no camps. None names the winner.
/// </summary>
public sealed class MatchHighlights : IHighlightSource
{
    public IEnumerable<Highlight> Find(HighlightContext context)
    {
        if (context.Insights.Finish.CoreRace)
        {
            yield return new Highlight(
                HighlightKind.CoreRace,
                null,
                new[] { "Core race finish" },
                "Core race"
            );
        }

        if (context.Insights.Comeback.Comeback)
        {
            yield return new Highlight(
                HighlightKind.Comeback,
                null,
                new[] { "Insane comeback" },
                "Comeback"
            );
        }

        foreach (TeamCamps team in context.Insights.Camps.Teams)
        {
            if (team.Drought)
            {
                string camps = team.Captures == 1 ? "camp" : "camps";
                yield return new Highlight(
                    HighlightKind.CampDrought,
                    null,
                    new[] { $"{ReplaySnapshot.TeamName(team.Team)} takes {team.Captures} {camps}" },
                    null
                );
            }
        }
    }
}
