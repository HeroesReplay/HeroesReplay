using Heroes.ReplayParser;
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

        Assert.Equal(65389750, entry.ReplayId);
        Assert.Equal("Volskaya Foundry - 65389750 - Storm League - Diamond", entry.Title);
        Assert.Contains(
            entry.DescriptionLines,
            line => line.Contains("replayID=65389750", System.StringComparison.Ordinal)
        );
        Assert.Equal("public", entry.PrivacyStatus);
    }

    [Fact]
    public void Create_UsesEnglishMapWhenTheReplayTitleIsLocalized()
    {
        var loaded = new LoadedReplay
        {
            ReplayId = 65396086,
            Replay = new Replay { Map = "용의 둥지", MapAlternativeName = "DragonShire" },
            HeroesProfileReplay = new HeroesProfileReplay
            {
                Id = 65396086,
                Map = "용의 둥지",
                Rank = "Platinum",
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            loaded,
            new YouTubeSettings { PrivacyStatus = "public", CategoryId = "20" }
        );

        Assert.Equal("Dragon Shire - 65396086 - Platinum", entry.Title);
        Assert.Contains(entry.Tags, tag => tag == "Dragon Shire");
        Assert.DoesNotContain(entry.Tags, tag => tag.Contains("용"));

        loaded.Replay.Map = "Le laboratoire de Braxis";
        loaded.Replay.MapAlternativeName = "BraxisHoldout";
        loaded.HeroesProfileReplay.Map = "Le laboratoire de Braxis";
        loaded.HeroesProfileReplay.Id = 65396084;
        loaded.ReplayId = 65396084;

        entry = YouTubeEntryBuilder.Create(
            loaded,
            new YouTubeSettings { PrivacyStatus = "public", CategoryId = "20" }
        );

        Assert.Equal("Braxis Holdout - 65396084 - Platinum", entry.Title);
    }
}
