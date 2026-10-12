using System;
using System.Collections.Generic;
using System.Linq;
using Heroes.ReplayParser;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Builds.Played;

/// <summary>
/// Maps a replay player's talent picks to the hero's catalog talents (#412). The replay names a
/// pick by the talent element id (<c>TalentChosen</c>), which is the catalog's
/// <see cref="HeroTalent.TalentId"/>; the button id is the fallback. A pick the catalog does not
/// know, or a second pick in a tier, is skipped.
/// </summary>
public static class PlayedBuilds
{
    public static PlayedBuild Read(Hero hero, Player player) =>
        Read(hero, player?.Talents?.Select(talent => talent?.TalentName));

    public static PlayedBuild Read(Hero hero, IEnumerable<string> talentNames)
    {
        if (hero == null)
        {
            return null;
        }

        var byTalentId = new Dictionary<string, HeroTalent>(StringComparer.Ordinal);
        var byButtonId = new Dictionary<string, HeroTalent>(StringComparer.Ordinal);
        foreach (HeroTalent talent in hero.Talents)
        {
            if (!string.IsNullOrWhiteSpace(talent.TalentId))
            {
                byTalentId.TryAdd(talent.TalentId, talent);
            }

            if (!string.IsNullOrWhiteSpace(talent.ButtonId))
            {
                byButtonId.TryAdd(talent.ButtonId, talent);
            }
        }

        var picks = new Dictionary<int, HeroTalent>();
        foreach (string name in talentNames ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string key = name.Trim();
            if (
                byTalentId.TryGetValue(key, out HeroTalent talent)
                || byButtonId.TryGetValue(key, out talent)
            )
            {
                picks.TryAdd(talent.Tier, talent);
            }
        }

        return new PlayedBuild(
            hero,
            picks.OrderBy(pick => pick.Key).Select(pick => pick.Value).ToArray()
        );
    }
}
