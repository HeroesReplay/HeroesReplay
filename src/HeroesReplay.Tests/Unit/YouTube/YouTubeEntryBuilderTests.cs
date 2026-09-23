using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeEntryBuilderTests
{
    [Fact]
    public void Create_IncludesMapReplayIdAndHeroesProfileLink()
    {
        var loaded = new LoadedReplay
        {
            ReplayId = 65389750,
            HeroesProfileReplay = new HeroesProfileReplay
            {
                Id = 65389750,
                Map = "Volskaya Foundry",
                GameType = "Storm League",
                Rank = "Diamond",
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            loaded,
            new YouTubeSettings { PrivacyStatus = "public", CategoryId = "20" }
        );

        Assert.Equal("Volskaya Foundry - 65389750 - Storm League - Diamond", entry.Title);
        Assert.Contains(
            entry.DescriptionLines,
            line => line.Contains("replayID=65389750", System.StringComparison.Ordinal)
        );
        Assert.Equal("public", entry.PrivacyStatus);
    }
}
