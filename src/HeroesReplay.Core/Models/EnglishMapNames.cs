using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Models;

public static class EnglishMapNames
{
    // Replay MapAlternativeName and the map-file id, not the localized title.
    private static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>
    {
        ["alteracpass"] = "Alterac Pass",
        ["battlefieldofeternity"] = "Battlefield of Eternity",
        ["blackheartsbay"] = "Blackheart's Bay",
        ["braxisholdout"] = "Braxis Holdout",
        ["braxisoutpost"] = "Braxis Outpost",
        ["controlpoints"] = "Sky Temple",
        ["crypts"] = "Tomb of the Spider Queen",
        ["cursedhollow"] = "Cursed Hollow",
        ["dragonshire"] = "Dragon Shire",
        ["hanamura"] = "Hanamura Temple",
        ["hauntedmines"] = "Haunted Mines",
        ["hauntedwoods"] = "Garden of Terror",
        ["industrialdistrict"] = "Industrial District",
        ["lostcavern"] = "Lost Cavern",
        ["shrines"] = "Infernal Shrines",
        ["silvercity"] = "Silver City",
        ["towersofdoom"] = "Towers of Doom",
        ["volskaya"] = "Volskaya Foundry",
        ["warheadjunction"] = "Warhead Junction",
        ["용의둥지"] = "Dragon Shire",
        ["lelaboratoiredebraxis"] = "Braxis Holdout",
    };

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

        string trimmed = map.Trim().Replace('\u2019', '\'').Replace('\u00A0', ' ');
        foreach (string name in Catalog)
        {
            if (string.Equals(name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        if (ShortNames.TryGetValue(Normalize(trimmed), out string english))
        {
            return english;
        }

        return trimmed;
    }

    private static string Normalize(string value)
    {
        var chars = new char[value.Length];
        int count = 0;
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                chars[count++] = char.ToLowerInvariant(c);
            }
        }

        return new string(chars, 0, count);
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
