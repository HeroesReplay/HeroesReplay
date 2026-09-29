using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeListingTests
{
    [Fact]
    public void ApplyMarker_AddsThePrefixOnce()
    {
        Assert.Equal(
            "[TEST] Infernal Shrines",
            YouTubeListing.ApplyMarker("Infernal Shrines", "[TEST]")
        );
        Assert.Equal(
            "[TEST] Infernal Shrines",
            YouTubeListing.ApplyMarker("[TEST] Infernal Shrines", "[TEST]")
        );
        Assert.Equal("Infernal Shrines", YouTubeListing.ApplyMarker("Infernal Shrines", " "));
    }

    [Fact]
    public void Stamp_OnThisKindOfMachine_IsPrivateAndMarked()
    {
        var entry = new YouTubeEntry { Title = "Cursed Hollow - 1", PrivacyStatus = "public" };

        YouTubeListing.StampForHost(entry, new YouTubeSettings(), "ASA-SERVER");

        Assert.Equal("private", entry.PrivacyStatus);
        Assert.Equal("[TEST] Cursed Hollow - 1", entry.Title);
    }

    [Fact]
    public void Stamp_OnTheProductionHost_KeepsTheConfiguredListing()
    {
        var entry = new YouTubeEntry { Title = "Cursed Hollow - 1", PrivacyStatus = "public" };

        YouTubeListing.StampForHost(
            entry,
            new YouTubeSettings { PrivacyStatus = "public" },
            "DESKTOP-8SJE72"
        );

        Assert.Equal("public", entry.PrivacyStatus);
        Assert.Equal("Cursed Hollow - 1", entry.Title);
    }
}
