using System.Collections.Generic;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// Highlights about the match's shape (#412): a core race and an insane comeback. None names the
/// winner, and none calls out a team.
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
    }
}
