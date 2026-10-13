using System.Linq;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// A full match title that leads with a highlight (#412):
/// <c>[X focus] - highlight - [Ft. X] - Map - Mode - Rank - [draft note] - ReplayId</c>.
/// Over the limit, the draft note goes first, then each shorter wording of the highlight is tried.
/// Null means no wording fits, and the caller builds the title without a highlight.
/// </summary>
public static class HighlightTitle
{
    public static string Compose(
        Highlight highlight,
        int maxLength,
        string lead,
        string feature,
        string map,
        string mode,
        string rank,
        string draft,
        string replayId
    )
    {
        if (highlight == null)
        {
            return null;
        }

        foreach (string wording in highlight.Variants)
        {
            foreach (string note in new[] { draft, null }.Distinct())
            {
                string title = Join(lead, wording, feature, map, mode, rank, note, replayId);
                if (title.Length <= maxLength)
                {
                    return title;
                }
            }
        }

        return null;
    }

    private static string Join(params string[] parts) =>
        string.Join(
            " - ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim())
        );
}
