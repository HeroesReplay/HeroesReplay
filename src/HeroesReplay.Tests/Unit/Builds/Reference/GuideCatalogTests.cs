using System;
using System.Linq;
using HeroesReplay.Core.Builds.Reference;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Builds.Reference;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class GuideCatalogTests
{
    [Fact]
    public void Embedded_HasEveryGuideAsFactsOnly()
    {
        GuideReference reference = GuideCatalog.Embedded().Reference;

        Assert.Equal("Icy Veins", reference.Source);
        Assert.Equal("https://www.icy-veins.com/heroes/", reference.SourceUrl);
        Assert.Equal(91, reference.Heroes.Count);
        Assert.Equal(296, reference.Heroes.Sum(guide => guide.Builds.Count));
        Assert.All(
            reference.Heroes,
            guide =>
            {
                Assert.StartsWith("Hero", guide.UnitId, StringComparison.Ordinal);
                Assert.Equal(
                    "https://www.icy-veins.com/heroes/" + guide.Slug + "-build-guide",
                    guide.GuideUrl
                );
                Assert.All(
                    guide.Builds,
                    build =>
                    {
                        Assert.EndsWith("Build", build.Name, StringComparison.OrdinalIgnoreCase);
                        Assert.Equal(7, build.Tiers.Count);
                        Assert.All(build.Tiers, tier => Assert.NotEmpty(tier.Recommended));
                    }
                );
            }
        );
    }

    [Fact]
    public void Find_UsesTheUnitIdThenTheEnglishName()
    {
        GuideCatalog catalog = GuideCatalog.Embedded();

        HeroGuide byUnit = catalog.Find(
            new Hero("Li Ming in another language", "HeroWizard", null, null)
        );
        HeroGuide byName = catalog.Find(new Hero("Li-Ming", null, null, null));

        Assert.Equal("li-ming", byUnit.Slug);
        Assert.Same(byUnit, byName);
        Assert.Contains(byUnit.Builds, build => build.Name == "Teleport Build");
        Assert.Null(catalog.Find(new Hero("Nobody", "HeroNobody", null, null)));
        Assert.Null(catalog.Find(null));
    }

    [Fact]
    public void Json_LeavesOutDefaultsAndEmptyListsAndReadsThemBack()
    {
        var reference = new GuideReference
        {
            Source = "Icy Veins",
            CheckedAt = new DateOnly(2026, 10, 12),
            Heroes = new[]
            {
                BuildFixtures.Guide(
                    BuildFixtures.Build(
                        "Teleport Build",
                        GuideTags.Recommended,
                        "ynn",
                        "ynn",
                        "ynn",
                        "yn",
                        "ynn",
                        "ynn",
                        "ynnn"
                    )
                ),
            },
        };

        string json = GuideReferenceJson.Write(reference);
        GuideReference read = GuideReferenceJson.Parse(json);

        Assert.DoesNotContain("situational", json, StringComparison.Ordinal);
        Assert.DoesNotContain("stale", json, StringComparison.Ordinal);
        Assert.DoesNotContain("calculatorUrl", json, StringComparison.Ordinal);
        Assert.Contains("\"checkedAt\": \"2026-10-12\"", json, StringComparison.Ordinal);
        GuideTier tier = read.Heroes[0].Builds[0].Tiers[0];
        Assert.Equal(new[] { "T01" }, tier.Recommended);
        Assert.Empty(tier.Situational);
        Assert.False(tier.Stale);
    }
}
