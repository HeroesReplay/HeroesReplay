using System;

namespace HeroesReplay.Core.Models;

public static class EnglishMapNames
{
    private static readonly string[] Catalog =
    {
        "Alterac Pass",
        "Battlefield of Eternity",
        "Blackheart's Bay",
        "Braxis Holdout",
        "Braxis Outpost",
        "Cursed Hollow",
        "Dragon Shire",
        "Garden of Terror",
        "Hanamura Temple",
        "Haunted Mines",
        "Industrial District",
        "Infernal Shrines",
        "Lost Cavern",
        "Silver City",
        "Sky Temple",
        "Tomb of the Spider Queen",
        "Towers of Doom",
        "Volskaya Foundry",
        "Warhead Junction",
    };

    public static string Prefer(string heroesProfileMap, string replayMap, string alternativeMap)
    {
        foreach (string candidate in new[] { heroesProfileMap, replayMap, alternativeMap })
        {
            string canonical = Canonical(candidate);
            if (IsCatalog(canonical))
            {
                return canonical;
            }
        }

        foreach (string candidate in new[] { heroesProfileMap, replayMap, alternativeMap })
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    public static string Canonical(string map)
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return null;
        }

        string trimmed = map.Trim().Replace('\u2019', '\'');
        foreach (string name in Catalog)
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return trimmed;
    }

    public static bool IsCatalog(string map)
    {
        if (string.IsNullOrWhiteSpace(map))
        {
            return false;
        }

        foreach (string name in Catalog)
        {
            if (string.Equals(name, map, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
