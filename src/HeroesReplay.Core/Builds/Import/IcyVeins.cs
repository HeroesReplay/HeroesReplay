using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Builds.Import;

/// <summary>
/// Icy Veins' Heroes of the Storm guide addresses (#412). A hero's slug is its English name in
/// lower case with accents, apostrophes and dots dropped and spaces as hyphens (<c>li-ming</c>,
/// <c>kaelthas</c>, <c>sgt-hammer</c>), except where the site chose otherwise.
/// </summary>
public static class IcyVeins
{
    public const string Source = "Icy Veins";
    public const string HeroesUrl = "https://www.icy-veins.com/heroes/";

    private static readonly Dictionary<string, string> Exceptions = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["D.Va"] = "dva",
        ["E.T.C."] = "e-t-c",
        ["Kel'Thuzad"] = "kel-thuzad",
    };

    public static string GuideUrl(string slug) => HeroesUrl + slug + "-build-guide";

    /// <summary>The talent calculator link for <paramref name="code"/> (<c>48.2!2232123</c>), or null.</summary>
    public static string CalculatorUrl(string slug, string code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : HeroesUrl + "talent-calculator/" + slug + "#" + code.Trim();

    public static string Slug(Hero hero) => hero == null ? null : Slug(hero.Name);

    public static string Slug(string englishName)
    {
        if (string.IsNullOrWhiteSpace(englishName))
        {
            return null;
        }

        string name = englishName.Trim();
        if (Exceptions.TryGetValue(name, out string slug))
        {
            return slug;
        }

        var builder = new StringBuilder();
        foreach (char character in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character is ' ' or '-' && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
