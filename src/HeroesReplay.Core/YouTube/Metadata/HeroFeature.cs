using System;
using System.Collections.Generic;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.YouTube.Metadata;

/// <summary>
/// The one new or recently released hero to name in a full-match title.
/// </summary>
public static class HeroFeature
{
    public static string Select(
        YouTubeTitleSettings titles,
        IReadOnlyList<Hero> catalog,
        IReadOnlyList<ReplayMediaPlayer> roster,
        DateTime? gameDateUtc
    )
    {
        if (titles == null || !titles.FeatureNewHeroes || roster == null || roster.Count == 0)
        {
            return null;
        }

        string[] configured = titles.RecentHeroList();
        string best = null;
        DateTime bestReleased = DateTime.MinValue;
        bool bestDated = false;
        int bestIndex = int.MaxValue;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (ReplayMediaPlayer player in roster)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.Hero))
            {
                continue;
            }

            string display = player.Hero.Trim();
            string key = HeroDraft.Key(display);
            if (key == null || !seen.Add(key))
            {
                continue;
            }

            Hero hero = catalog == null ? null : HeroDraft.Find(catalog, display);
            int configIndex = ConfigIndex(configured, display, hero);
            DateTime? released = hero?.ReleaseDate;
            if (configIndex < 0 && !InWindow(released, gameDateUtc, titles.RecentHeroDays))
            {
                continue;
            }

            bool dated = released.HasValue;
            DateTime when = released ?? DateTime.MinValue;
            int index = configIndex < 0 ? int.MaxValue : configIndex;
            if (!Better(dated, when, index, best != null, bestDated, bestReleased, bestIndex))
            {
                continue;
            }

            best = configIndex >= 0 ? configured[configIndex].Trim() : display;
            bestDated = dated;
            bestReleased = when;
            bestIndex = index;
        }

        return string.IsNullOrWhiteSpace(best) ? null : best;
    }

    private static bool Better(
        bool dated,
        DateTime when,
        int index,
        bool hasBest,
        bool bestDated,
        DateTime bestWhen,
        int bestIndex
    )
    {
        if (!hasBest)
        {
            return true;
        }

        if (dated != bestDated)
        {
            return dated;
        }

        if (dated && when != bestWhen)
        {
            return when > bestWhen;
        }

        return index < bestIndex;
    }

    private static bool InWindow(DateTime? released, DateTime? played, int days)
    {
        if (days <= 0 || released == null || played == null)
        {
            return false;
        }

        int age = (played.Value.Date - released.Value.Date).Days;
        return age >= 0 && age <= days;
    }

    private static int ConfigIndex(string[] names, string display, Hero hero)
    {
        if (names == null)
        {
            return -1;
        }

        for (int i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(names[i]))
            {
                continue;
            }

            if (HeroDraft.SameName(names[i], display))
            {
                return i;
            }

            if (
                hero != null
                && (
                    HeroDraft.SameName(names[i], hero.Name)
                    || HeroDraft.SameName(names[i], hero.HyperlinkId)
                    || HeroDraft.SameName(names[i], hero.UnitId)
                    || HeroDraft.SameName(names[i], hero.AttributeId)
                )
            )
            {
                return i;
            }
        }

        return -1;
    }
}
