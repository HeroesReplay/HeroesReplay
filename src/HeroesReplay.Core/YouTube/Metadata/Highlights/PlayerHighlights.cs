using System.Collections.Generic;
using HeroesReplay.Core.Analysis.Insights.Focused;
using HeroesReplay.Core.Analysis.Insights.Players;
using HeroesReplay.Core.Clips;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// Highlights about one hero (#412): a pentakill, a deathless carry, a kill leader, the MVP, and
/// the hero the camera followed most. A strong, distinctive guide build is named with the hero
/// (<c>Deathless Teleport Li-Ming 12/0/9</c>); shorter wordings drop the build, then the score.
/// </summary>
public sealed class PlayerHighlights : IHighlightSource
{
    public IEnumerable<Highlight> Find(HighlightContext context)
    {
        foreach (TeamKillClip clip in context.Pentakills)
        {
            if (clip.Kind != TeamKillClips.PentakillKind || string.IsNullOrWhiteSpace(clip.Hero))
            {
                continue;
            }

            PlayerLine line = context.LineFor(clip.Hero);
            string label = line == null ? null : context.BuildLabel(line.PlayerIndex, clip.Hero);
            yield return new Highlight(
                HighlightKind.Pentakill,
                clip.Hero,
                Variants(label == null ? null : label + " pentakill", clip.Hero + " pentakill"),
                "Pentakill"
            );
        }

        foreach (Standout standout in context.Insights.Players.Standouts)
        {
            PlayerLine line = standout.Player;
            string label = context.BuildLabel(line.PlayerIndex, line.Hero);
            if (standout.Kind == StandoutKind.DeathlessCarry)
            {
                yield return new Highlight(
                    HighlightKind.DeathlessCarry,
                    line.Hero,
                    Variants(
                        label == null ? null : $"Deathless {label} {line.Kda}",
                        $"Deathless {line.Hero} {line.Kda}",
                        $"Deathless {line.Hero}"
                    ),
                    "Deathless"
                );
            }
            else if (standout.Kind == StandoutKind.KillLeader)
            {
                yield return new Highlight(
                    HighlightKind.KillLeader,
                    line.Hero,
                    Variants(
                        label == null ? null : $"{label} {line.Kda}",
                        $"{line.Hero} {line.Kda}"
                    ),
                    null
                );
            }
        }

        PlayerLine mvp = context.Insights.Players.Mvp;
        if (mvp?.Hero != null && !mvp.IsAi)
        {
            string label = context.BuildLabel(mvp.PlayerIndex, mvp.Hero);
            yield return new Highlight(
                HighlightKind.Mvp,
                mvp.Hero,
                Variants(
                    label == null ? null : $"MVP {label} {mvp.Kda}",
                    $"MVP {mvp.Hero} {mvp.Kda}",
                    $"MVP {mvp.Hero}"
                ),
                "MVP"
            );
        }

        FocusShare top = context.Insights.Focus.Top;
        PlayerLine followed = top == null ? null : context.Line(top.PlayerIndex);
        if (followed?.Hero != null && !followed.IsAi)
        {
            string label = context.BuildLabel(followed.PlayerIndex, followed.Hero);
            yield return new Highlight(
                HighlightKind.MostFollowed,
                followed.Hero,
                Variants(
                    label == null ? null : $"{label} {followed.Kda}",
                    $"{followed.Hero} {followed.Kda}",
                    followed.Hero
                ),
                null
            );
        }
    }

    /// <summary>The wordings that exist, longest first, without repeats.</summary>
    internal static IReadOnlyList<string> Variants(params string[] wordings)
    {
        var variants = new List<string>();
        foreach (string wording in wordings)
        {
            if (!string.IsNullOrWhiteSpace(wording) && !variants.Contains(wording))
            {
                variants.Add(wording);
            }
        }

        return variants;
    }
}
