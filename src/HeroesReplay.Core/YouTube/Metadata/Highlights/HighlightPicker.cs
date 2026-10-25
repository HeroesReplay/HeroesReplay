using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// Chooses the one highlight a title leads with (#412): every source offers its highlights, and
/// the first kind in <see cref="HighlightSettings.Order"/> that the match has wins.
/// </summary>
public sealed class HighlightPicker
{
    private readonly IReadOnlyList<IHighlightSource> sources;

    public HighlightPicker(IEnumerable<IHighlightSource> sources)
    {
        this.sources = sources?.ToArray() ?? throw new ArgumentNullException(nameof(sources));
    }

    /// <summary>The player, match, lane and feat sources.</summary>
    public static HighlightPicker Default { get; } =
        new(
            new IHighlightSource[]
            {
                new PlayerHighlights(),
                new MatchHighlights(),
                new LaneHighlights(),
                new FeatHighlights(),
            }
        );

    /// <summary>The highlight to lead with, or null. <paramref name="allowed"/> filters candidates (a hero the title already names).</summary>
    public Highlight Pick(HighlightContext context, Func<Highlight, bool> allowed = null)
    {
        if (context == null)
        {
            return null;
        }

        List<Highlight> candidates = sources
            .SelectMany(source => source.Find(context))
            .Where(highlight =>
                highlight.Variants.Count > 0 && (allowed == null || allowed(highlight))
            )
            .ToList();
        foreach (HighlightKind kind in context.Settings.Kinds())
        {
            Highlight found = candidates.FirstOrDefault(highlight => highlight.Kind == kind);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
