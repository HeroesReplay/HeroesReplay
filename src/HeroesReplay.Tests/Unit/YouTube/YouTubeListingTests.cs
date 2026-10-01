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
    public void Stamp_WithTheDevSettings_IsPrivateAndMarked()
    {
        var entry = new YouTubeEntry { Title = "Cursed Hollow - 1", PrivacyStatus = "public" };

        YouTubeListing.Stamp(
            entry,
            new YouTubeSettings { PrivacyStatus = "private", TitlePrefix = "[TEST]" }
        );

        Assert.Equal("private", entry.PrivacyStatus);
        Assert.Equal("[TEST] Cursed Hollow - 1", entry.Title);
    }

    [Fact]
    public void Stamp_WithTheProdSettings_KeepsTheConfiguredListing()
    {
        var entry = new YouTubeEntry { Title = "Cursed Hollow - 1", PrivacyStatus = "public" };

        YouTubeListing.Stamp(entry, new YouTubeSettings { PrivacyStatus = "public" });

        Assert.Equal("public", entry.PrivacyStatus);
        Assert.Equal("Cursed Hollow - 1", entry.Title);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("public", true)]
    [InlineData(" Public ", true)]
    [InlineData("private", false)]
    [InlineData("unlisted", false)]
    public void IsPublic_ReadsThePrivacySetting(string privacy, bool expected)
    {
        Assert.Equal(
            expected,
            YouTubeListing.IsPublic(new YouTubeSettings { PrivacyStatus = privacy })
        );
    }
}
