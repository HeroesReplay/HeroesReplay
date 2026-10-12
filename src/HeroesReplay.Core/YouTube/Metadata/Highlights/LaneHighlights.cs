using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights.Lanes;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// The solo lane (#412), named only at High confidence: the team's one soaker laned against the
/// other team's soaker. Both teams: <c>Solo lane Chen vs Thrall</c>. One: <c>Solo laner Dehaka</c>.
/// </summary>
public sealed class LaneHighlights : IHighlightSource
{
    public IEnumerable<Highlight> Find(HighlightContext context)
    {
        List<TeamLanes> sure = HighConfidence(context).ToList();
        if (sure.Count == 2)
        {
            yield return new Highlight(
                HighlightKind.SoloLane,
                null,
                new[] { $"Solo lane {sure[0].SoloLaner.Hero} vs {sure[1].SoloLaner.Hero}" },
                "Solo lane"
            );
        }
        else if (sure.Count == 1)
        {
            yield return new Highlight(
                HighlightKind.SoloLane,
                sure[0].SoloLaner.Hero,
                new[] { $"Solo laner {sure[0].SoloLaner.Hero}" },
                "Solo lane"
            );
        }
    }

    internal static IEnumerable<TeamLanes> HighConfidence(HighlightContext context) =>
        context
            .Insights.Lanes.Teams.Where(team =>
                team.Confidence == LaneConfidence.High && team.SoloLaner?.Hero != null
            )
            .OrderBy(team => team.Team);
}
