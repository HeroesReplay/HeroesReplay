using System;
using System.Collections.Generic;
using System.Globalization;
using HeroesReplay.Core.Analysis.Insights.Awards;
using HeroesReplay.Core.Analysis.Insights.Bursts;
using HeroesReplay.Core.Analysis.Insights.Objectives;

namespace HeroesReplay.Core.YouTube.Metadata.Highlights;

/// <summary>
/// Highlights about one hero's feats (#512): repeated triple kills, the map objective's standout,
/// and a game award (Hat Trick, or one with a high count). Each names the hero with what they did
/// (<c>Kael'thas 2 triple kills</c>, <c>Valla 52% of Immortal damage</c>, <c>Rehgar 9 clutch heals</c>);
/// a strong guide build is named first, and the shorter wordings drop it.
/// </summary>
public sealed class FeatHighlights : IHighlightSource
{
    public IEnumerable<Highlight> Find(HighlightContext context)
    {
        foreach (PlayerBursts player in context.Insights.Bursts.Players)
        {
            if (player.Hero == null || player.Bursts.Count < context.Settings.TripleKillMinBursts)
            {
                continue;
            }

            string label = context.BuildLabel(player.PlayerIndex, player.Hero);
            string feat = TripleKills(player.Bursts.Count);
            yield return new Highlight(
                HighlightKind.TripleKill,
                player.Hero,
                PlayerHighlights.Variants(
                    label == null ? null : $"{label} {feat}",
                    $"{player.Hero} {feat}"
                ),
                "Triple kill"
            );
        }

        ObjectiveInsights objective = context.Insights.Objectives;
        if (objective.Standout && objective.Leader?.Hero != null && objective.Label != null)
        {
            ObjectiveLeader leader = objective.Leader;
            string label = context.BuildLabel(leader.PlayerIndex, leader.Hero);
            string feat = Share(leader.Share) + " of " + objective.Label;
            yield return new Highlight(
                HighlightKind.ObjectiveStandout,
                leader.Hero,
                PlayerHighlights.Variants(
                    label == null ? null : $"{label} {feat}",
                    $"{leader.Hero} {feat}"
                ),
                "Objective"
            );
        }

        foreach (PlayerAward award in context.Insights.Awards.Awards)
        {
            if (award.Hero == null)
            {
                continue;
            }

            string label = context.BuildLabel(award.PlayerIndex, award.Hero);
            string feat = Count(award);
            yield return new Highlight(
                HighlightKind.Award,
                award.Hero,
                PlayerHighlights.Variants(
                    label == null ? null : $"{label} {feat}",
                    $"{award.Hero} {feat}"
                ),
                Tag(award.Kind)
            );
        }
    }

    /// <summary><c>2 triple kills</c>, or <c>triple kill</c> for one.</summary>
    internal static string TripleKills(int bursts) =>
        bursts == 1
            ? "triple kill"
            : bursts.ToString(CultureInfo.InvariantCulture) + " triple kills";

    /// <summary>The share rounded down, so a title never claims more than the replay holds: <c>52%</c>.</summary>
    internal static string Share(double share) =>
        ((int)Math.Floor(share * 100)).ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary><c>hat trick</c>, <c>9 clutch heals</c>, <c>5 vengeances</c>, <c>6 escapes</c>.</summary>
    internal static string Count(PlayerAward award)
    {
        string value = award.Value.ToString(CultureInfo.InvariantCulture);
        return award.Kind switch
        {
            AwardKind.HatTrick => "hat trick",
            AwardKind.ClutchHealer => value + " clutch heals",
            AwardKind.Avenger => value + " vengeances",
            _ => value + " escapes",
        };
    }

    private static string Tag(AwardKind kind) =>
        kind switch
        {
            AwardKind.HatTrick => "Hat trick",
            AwardKind.ClutchHealer => "Clutch healer",
            AwardKind.Avenger => "Avenger",
            _ => "Escape artist",
        };
}
