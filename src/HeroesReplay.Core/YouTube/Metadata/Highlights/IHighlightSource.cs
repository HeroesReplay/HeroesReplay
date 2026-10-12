using System.Collections.Generic;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>Offers the highlights of one family (players, the match, lanes). The picker chooses.</summary>
public interface IHighlightSource
{
    IEnumerable<Highlight> Find(HighlightContext context);
}
