using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.HeroesData;
using HeroesReplay.Core.Shared;

namespace HeroesReplay.Core.Builds.Import;

/// <summary>The checked reference, or the reasons it was refused.</summary>
/// <param name="Reference">The reference to write. Null when there are errors.</param>
/// <param name="Errors">Problems that refuse the whole import: an unknown hero, a build without seven tiers, a bad mark.</param>
/// <param name="Warnings">Problems the reference keeps: a stale tier, a renamed talent, a hero with no guide.</param>
public sealed record GuideImportResult(
    GuideReference Reference,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings
);

/// <summary>
/// Checks the skill's extract against the game's talents and turns it into the reference (#412).
/// Each tier's marks are the game's talent columns, so a tier whose mark count differs from the
/// catalog tier predates a talent change: it is kept as stale and never scored. A recommended
/// talent whose name does not match the talent in that column is reported.
/// </summary>
public static class GuideImporter
{
    private static readonly string[] UpdatedFormats = { "MMM dd, yyyy", "MMM d, yyyy" };

    public static GuideImportResult Import(
        IReadOnlyList<GuideExtractHero> extract,
        IReadOnlyList<Hero> catalog,
        DateOnly checkedAt,
        string gameVersion
    )
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        Dictionary<string, Hero> heroesBySlug = HeroesBySlug(catalog, errors);
        var guides = new Dictionary<string, HeroGuide>(StringComparer.Ordinal);

        foreach (GuideExtractHero page in extract ?? Array.Empty<GuideExtractHero>())
        {
            string slug = page?.Slug?.Trim();
            if (string.IsNullOrEmpty(slug))
            {
                errors.Add("A guide has no slug.");
                continue;
            }

            if (!heroesBySlug.TryGetValue(slug, out Hero hero))
            {
                errors.Add($"Guide '{slug}' names no hero in the catalog.");
                continue;
            }

            if (guides.ContainsKey(slug))
            {
                warnings.Add($"Guide '{slug}' was read twice; the first read is kept.");
                continue;
            }

            guides[slug] = Guide(page, hero, slug, errors, warnings);
        }

        foreach (
            KeyValuePair<string, Hero> hero in heroesBySlug.Where(pair =>
                !guides.ContainsKey(pair.Key)
            )
        )
        {
            warnings.Add($"{hero.Value.Name} has no guide ('{hero.Key}').");
        }

        if (errors.Count > 0)
        {
            return new GuideImportResult(null, errors, warnings);
        }

        var reference = new GuideReference
        {
            Source = IcyVeins.Source,
            SourceUrl = IcyVeins.HeroesUrl,
            CheckedAt = checkedAt,
            GameVersion = gameVersion,
            Heroes = guides
                .Values.OrderBy(guide => guide.Hero, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
        return new GuideImportResult(reference, errors, warnings);
    }

    /// <summary>A talent name or slug as lower-case letters and digits only: <c>C'Thun's Gift</c> and <c>c-thuns-gift</c> agree.</summary>
    internal static string NameKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (char character in name.Normalize(NormalizationForm.FormD))
        {
            if (
                char.IsLetterOrDigit(character)
                && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
            )
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static Dictionary<string, Hero> HeroesBySlug(
        IReadOnlyList<Hero> catalog,
        List<string> errors
    )
    {
        var bySlug = new Dictionary<string, Hero>(StringComparer.Ordinal);
        foreach (Hero hero in catalog ?? Array.Empty<Hero>())
        {
            string slug = IcyVeins.Slug(hero);
            if (string.IsNullOrEmpty(slug))
            {
                continue;
            }

            if (!bySlug.TryAdd(slug, hero))
            {
                errors.Add(
                    $"Two catalog heroes share the slug '{slug}': {bySlug[slug].Name} and {hero.Name}."
                );
            }
        }

        return bySlug;
    }

    private static HeroGuide Guide(
        GuideExtractHero page,
        Hero hero,
        string slug,
        List<string> errors,
        List<string> warnings
    )
    {
        List<HeroTalent>[] tiers = Tiers(hero);
        var builds = new List<GuideBuild>();
        foreach (GuideExtractBuild build in page.Builds ?? Array.Empty<GuideExtractBuild>())
        {
            string name = build?.Name?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                errors.Add($"{hero.Name}: a build has no name.");
                continue;
            }

            IReadOnlyList<GuideExtractTier> extractTiers =
                build.Tiers ?? Array.Empty<GuideExtractTier>();
            if (extractTiers.Count != HeroTalents.TierCount)
            {
                errors.Add(
                    $"{hero.Name} {name}: {extractTiers.Count} tiers, not {HeroTalents.TierCount}."
                );
                continue;
            }

            var guideTiers = new List<GuideTier>();
            for (int tier = 0; tier < HeroTalents.TierCount; tier++)
            {
                guideTiers.Add(
                    Tier(hero, name, tier, extractTiers[tier], tiers[tier], errors, warnings)
                );
            }

            builds.Add(
                new GuideBuild
                {
                    Name = name,
                    Tag = string.IsNullOrWhiteSpace(build.Tag) ? null : build.Tag.Trim(),
                    CalculatorUrl = IcyVeins.CalculatorUrl(slug, build.Calculator),
                    Tiers = guideTiers,
                }
            );
        }

        if (builds.Count == 0)
        {
            warnings.Add($"{hero.Name}: the guide has no builds.");
        }

        return new HeroGuide
        {
            Hero = hero.Name,
            UnitId = hero.UnitId,
            Slug = slug,
            GuideUrl = IcyVeins.GuideUrl(slug),
            GuideUpdated = Updated(page.Updated, hero, warnings),
            Builds = builds,
        };
    }

    private static GuideTier Tier(
        Hero hero,
        string build,
        int tier,
        GuideExtractTier extract,
        List<HeroTalent> row,
        List<string> errors,
        List<string> warnings
    )
    {
        int level = int.TryParse(
            extract?.Label,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int label
        )
            ? label
            : 0;
        string marks = extract?.Marks?.Trim() ?? string.Empty;
        if (marks.Any(mark => mark is not ('y' or 's' or 'n')))
        {
            errors.Add($"{hero.Name} {build} tier {tier + 1}: marks '{marks}' are not y, s, or n.");
            return new GuideTier { Level = level, Stale = true };
        }

        if (marks.Length != row.Count)
        {
            warnings.Add(
                $"{hero.Name} {build} tier {tier + 1}: {marks.Length} marks for {row.Count} talents. The tier is stale."
            );
            return new GuideTier { Level = level, Stale = true };
        }

        var recommended = new List<HeroTalent>();
        var situational = new List<HeroTalent>();
        for (int column = 0; column < marks.Length; column++)
        {
            if (marks[column] == 'y')
            {
                recommended.Add(row[column]);
            }
            else if (marks[column] == 's')
            {
                situational.Add(row[column]);
            }
        }

        foreach (string slug in extract?.Recommended ?? Array.Empty<string>())
        {
            if (!recommended.Any(talent => NameKey(talent.Name) == NameKey(slug)))
            {
                warnings.Add(
                    $"{hero.Name} {build} tier {tier + 1}: the guide recommends '{slug}', the column holds "
                        + string.Join(" or ", recommended.Select(talent => talent.Name))
                        + "."
                );
            }
        }

        return new GuideTier
        {
            Level = level,
            Recommended = recommended.Select(talent => talent.TalentId).ToArray(),
            Situational = situational.Select(talent => talent.TalentId).ToArray(),
        };
    }

    private static List<HeroTalent>[] Tiers(Hero hero)
    {
        var tiers = new List<HeroTalent>[HeroTalents.TierCount];
        for (int tier = 0; tier < tiers.Length; tier++)
        {
            tiers[tier] = hero
                .Talents.Where(talent => talent.Tier == tier)
                .OrderBy(talent => talent.Column)
                .ToList();
        }

        return tiers;
    }

    private static DateOnly? Updated(string text, Hero hero, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (
            DateOnly.TryParseExact(
                text.Trim(),
                UpdatedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly day
            )
        )
        {
            return day;
        }

        warnings.Add($"{hero.Name}: the guide date '{text}' did not read.");
        return null;
    }
}
