using System.Collections.Generic;
using System.Linq;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// The title of a focus-hero video (#433), one player's whole match:
/// <c>Teleport Li-Ming gameplay - 12/0/9 - Alterac Pass - Storm League - Diamond - [draft note] - ReplayId</c>.
/// Over the limit, the draft note goes first, then the rank, then the score, then the build in
/// the lead (<c>Li-Ming gameplay</c>). Null means even that does not fit.
/// </summary>
public static class FocusTitle
{
    public const string Suffix = "gameplay";

    /// <summary><c>Teleport Li-Ming gameplay</c>, or <c>Li-Ming gameplay</c> without a build label.</summary>
    public static string Lead(string hero, string buildLabel) =>
        string.IsNullOrWhiteSpace(buildLabel)
            ? hero.Trim() + " " + Suffix
            : buildLabel.Trim() + " " + Suffix;

    public static string Compose(
        int maxLength,
        string hero,
        string buildLabel,
        string kda,
        string map,
        string mode,
        string rank,
        string draft,
        string replayId
    )
    {
        if (string.IsNullOrWhiteSpace(hero))
        {
            return null;
        }

        string full = Lead(hero, buildLabel);
        string plain = Lead(hero, null);
        IEnumerable<string[]> attempts = new[]
        {
            new[] { full, kda, map, mode, rank, draft, replayId },
            new[] { full, kda, map, mode, rank, null, replayId },
            new[] { full, kda, map, mode, null, null, replayId },
            new[] { full, null, map, mode, null, null, replayId },
            new[] { plain, null, map, mode, null, null, replayId },
        };
        foreach (string[] parts in attempts)
        {
            string title = string.Join(
                " - ",
                parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim())
            );
            if (title.Length <= maxLength)
            {
                return title;
            }
        }

        return null;
    }
}
