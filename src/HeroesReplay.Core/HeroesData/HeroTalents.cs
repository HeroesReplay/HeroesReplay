using System;
using System.Collections.Generic;
using System.Linq;
using HeroesReplay.Core.Shared;
using ElementHero = Heroes.Element.Models.Hero;
using ElementTalent = Heroes.Element.Models.AbilityTalents.Talent;
using ElementTier = Heroes.Element.Models.Types.TalentTier;

namespace HeroesReplay.Core.HeroesData;

/// <summary>
/// Reads a heroes-data hero's talents into <see cref="HeroTalent"/> values (#412): the seven
/// tiers in level order, each talent with its column and English name.
/// </summary>
public static class HeroTalents
{
    /// <summary>The number of talent tiers every hero has.</summary>
    public const int TierCount = 7;

    private static readonly ElementTier[] Tiers =
    {
        ElementTier.Level1,
        ElementTier.Level4,
        ElementTier.Level7,
        ElementTier.Level10,
        ElementTier.Level13,
        ElementTier.Level16,
        ElementTier.Level20,
    };

    public static IReadOnlyList<HeroTalent> Read(ElementHero hero)
    {
        if (hero?.Talents == null)
        {
            return Array.Empty<HeroTalent>();
        }

        var talents = new List<HeroTalent>();
        for (int tier = 0; tier < Tiers.Length; tier++)
        {
            if (!hero.Talents.TryGetValue(Tiers[tier], out IList<ElementTalent> row) || row == null)
            {
                continue;
            }

            foreach (ElementTalent talent in row.Where(talent => talent != null).OrderBy(Column))
            {
                talents.Add(
                    new HeroTalent(
                        talent.TalentElementId,
                        talent.ButtonElementId,
                        tier,
                        Column(talent),
                        Plain(talent.Name?.PlainText),
                        talent.Icon
                    )
                );
            }
        }

        return talents;
    }

    private static int Column(ElementTalent talent) => talent.Column;

    private static string Plain(string text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
