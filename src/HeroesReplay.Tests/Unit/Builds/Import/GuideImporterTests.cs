using System;
using System.Linq;
using HeroesReplay.Core.Builds.Import;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Builds.Import;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class GuideImporterTests
{
    private static readonly DateOnly Checked = new(2026, 10, 12);

    [Fact]
    public void Import_TurnsEachTiersMarksIntoTalentIdsByColumn()
    {
        GuideImportResult result = Import(
            Page("li-ming", Tiers("nys", "ynn", "nny", "sy", "ynn", "nyn", "nsyn"))
        );

        Assert.Empty(result.Errors);
        HeroGuide guide = Assert.Single(result.Reference.Heroes);
        Assert.Equal("Li-Ming", guide.Hero);
        Assert.Equal("HeroWizard", guide.UnitId);
        Assert.Equal("https://www.icy-veins.com/heroes/li-ming-build-guide", guide.GuideUrl);
        Assert.Equal(new DateOnly(2020, 10, 27), guide.GuideUpdated);
        GuideBuild build = Assert.Single(guide.Builds);
        Assert.Equal(
            "https://www.icy-veins.com/heroes/talent-calculator/li-ming#48.2!2232123",
            build.CalculatorUrl
        );
        Assert.Equal(new[] { "T02" }, build.Tiers[0].Recommended);
        Assert.Equal(new[] { "T03" }, build.Tiers[0].Situational);
        Assert.Equal(new[] { "T31" }, build.Tiers[3].Situational);
        Assert.Equal(new[] { "T63" }, build.Tiers[6].Recommended);
        Assert.Equal(Checked, result.Reference.CheckedAt);
        Assert.Equal("2.57.0.98304", result.Reference.GameVersion);
    }

    [Fact]
    public void Import_KeepsATierWithAnotherTalentCountAsStale()
    {
        GuideImportResult result = Import(
            Page("li-ming", Tiers("nys", "ynn", "nny", "syn", "ynn", "nyn", "nsyn"))
        );

        GuideTier tier = result.Reference.Heroes[0].Builds[0].Tiers[3];
        Assert.True(tier.Stale);
        Assert.Empty(tier.Recommended);
        Assert.Contains(
            result.Warnings,
            warning =>
                warning.Contains("tier 4", StringComparison.Ordinal)
                && warning.Contains("stale", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Import_RefusesAnUnknownHeroABuildWithoutSevenTiersAndABadMark()
    {
        Assert.Contains(
            "names no hero",
            Assert.Single(
                Import(
                    Page("nobody", Tiers("ynn", "ynn", "ynn", "yn", "ynn", "ynn", "ynnn"))
                ).Errors
            ),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "6 tiers",
            Assert.Single(
                Import(Page("li-ming", Tiers("ynn", "ynn", "ynn", "yn", "ynn", "ynn"))).Errors
            ),
            StringComparison.Ordinal
        );
        GuideImportResult bad = Import(
            Page("li-ming", Tiers("y?n", "ynn", "ynn", "yn", "ynn", "ynn", "ynnn"))
        );
        Assert.Null(bad.Reference);
        Assert.Contains("not y, s, or n", Assert.Single(bad.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WarnsWhenTheRecommendedNameIsNotTheColumnsTalent()
    {
        GuideExtractTier[] tiers = Tiers("nys", "ynn", "nny", "sy", "ynn", "nyn", "nsyn");
        tiers[0] = tiers[0] with { Recommended = new[] { "talent-0-2" } };
        tiers[1] = tiers[1] with { Recommended = new[] { "calamity" } };

        GuideImportResult result = Import(Page("li-ming", tiers));

        string warning = Assert.Single(result.Warnings);
        Assert.Contains("tier 2", warning, StringComparison.Ordinal);
        Assert.Contains("'calamity'", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Import_WarnsForACatalogHeroWithoutAGuide()
    {
        GuideImportResult result = GuideImporter.Import(
            new[] { Page("li-ming", Tiers("ynn", "ynn", "ynn", "yn", "ynn", "ynn", "ynnn")) },
            new[] { BuildFixtures.Hero(), BuildFixtures.Hero("Kel'Thuzad", "HeroKelThuzad") },
            Checked,
            "2.57.0.98304"
        );

        Assert.Contains("Kel'Thuzad has no guide ('kel-thuzad').", result.Warnings);
    }

    [Theory]
    [InlineData("C'Thun's Gift", "c-thuns-gift")]
    [InlineData("Jug of 1,000 Cups", "jug-of-1-000-cups")]
    [InlineData("Taz'dingo!", "taz-dingo")]
    [InlineData("Rejuvenescência", "rejuvenescencia")]
    public void NameKey_IgnoresPunctuationAndAccents(string name, string slug)
    {
        Assert.Equal(GuideImporter.NameKey(name), GuideImporter.NameKey(slug));
    }

    private static GuideImportResult Import(GuideExtractHero page) =>
        GuideImporter.Import(
            new[] { page },
            new Hero[] { BuildFixtures.Hero() },
            Checked,
            "2.57.0.98304"
        );

    private static GuideExtractHero Page(string slug, GuideExtractTier[] tiers) =>
        new()
        {
            Slug = slug,
            Updated = "Oct 27, 2020",
            Builds = new[]
            {
                new GuideExtractBuild
                {
                    Name = "Teleport Build",
                    Tag = "Recommended",
                    Calculator = "48.2!2232123",
                    Tiers = tiers,
                },
            },
        };

    private static GuideExtractTier[] Tiers(params string[] marks) =>
        marks
            .Select(
                (row, tier) =>
                    new GuideExtractTier
                    {
                        Label = tier.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Marks = row,
                    }
            )
            .ToArray();
}
