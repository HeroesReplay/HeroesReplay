using System;
using System.Linq;
using HeroesReplay.Core.Analysis.Insights;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Metadata.Highlights;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata.Highlights;

/// <summary>Highlights through the full match title and description (#412).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class FullMatchHighlightTests
{
    [Fact]
    public void Enabled_TheTitleLeadsWithTheHighlight_AndTheDescriptionCarriesTheInsightLines()
    {
        FullMatchMetadata metadata = Build(HighlightFixtures.Insights(), enabled: true);

        Assert.Equal(
            "Deathless Teleport Li-Ming 12/0/9 - Garden of Terror - Storm League - Master - 65581797",
            metadata.Title
        );
        Assert.Equal(metadata.Title, metadata.Highlight);
        Assert.Contains(
            "Highlights: Li-Ming 12/0/9 with no deaths; MVP Orphea 7/1/5",
            metadata.DescriptionLines
        );
        Assert.Contains(
            "Camps: Blue 9 (4 on Red's side), Red 6 (0 on Blue's side)",
            metadata.DescriptionLines
        );
        Assert.Equal(
            "Talent builds (closest Icy Veins guide build, https://www.icy-veins.com/heroes/):",
            metadata.DescriptionLines.First(line =>
                line.StartsWith("Talent builds", StringComparison.Ordinal)
            )
        );
        Assert.Contains("Deathless", metadata.Tags);
        Assert.Contains("Teleport Li-Ming", metadata.Tags);
        Assert.Equal("9", metadata.TemplateVersion);
    }

    [Fact]
    public void Disabled_OrWithoutInsights_TheTitleIsAsBefore()
    {
        const string Before = "Garden of Terror - Storm League - Master - 65581797";

        Assert.Equal(Before, Build(HighlightFixtures.Insights(), enabled: false).Title);
        Assert.Equal(Before, Build(null, enabled: true).Title);
        Assert.Null(Build(HighlightFixtures.Insights(), enabled: false).Highlight);
        Assert.DoesNotContain(
            Build(HighlightFixtures.Insights(), enabled: false).DescriptionLines,
            line => line.StartsWith("Talent builds", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ANamedPlayerRequest_KeepsItsLead_AndTakesOnlyAHighlightAboutNoOneHero()
    {
        FullMatchMetadata comeback = Build(
            HighlightFixtures.Insights(comeback: true),
            enabled: true,
            namedHero: "Orphea"
        );
        FullMatchMetadata standout = Build(
            HighlightFixtures.Insights(),
            enabled: true,
            namedHero: "Orphea"
        );

        Assert.Equal(
            "Orphea focus - Insane comeback - Garden of Terror - Storm League - Master - 65581797",
            comeback.Title
        );
        Assert.Equal(
            "Orphea focus - Garden of Terror - Storm League - Master - 65581797",
            standout.Title
        );
    }

    private static FullMatchMetadata Build(
        MatchInsights insights,
        bool enabled,
        string namedHero = null
    ) =>
        FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 65581797,
                Map = "Garden of Terror",
                GameMode = "Storm League",
                Rank = "Master",
                FocusHero = namedHero,
                NamedPlayer = namedHero != null,
                Insights = insights,
            },
            new FullMatchMetadataOptions
            {
                Titles = new YouTubeTitleSettings
                {
                    Highlights = new HighlightSettings { Enabled = enabled },
                },
            }
        );
}
