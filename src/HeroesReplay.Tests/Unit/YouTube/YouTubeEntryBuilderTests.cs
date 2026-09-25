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
        Assert.Equal("Volskaya Foundry", entry.Map);
        Assert.Equal("Storm League", entry.GameType);
        Assert.Equal("Diamond", entry.Rank);
        Assert.Equal("Volskaya Foundry - 65389750 - Storm League - Diamond", entry.Title);
        Assert.Contains(
            entry.DescriptionLines,
            line => line.Contains("replayID=65389750", System.StringComparison.Ordinal)
        );
        Assert.Equal("public", entry.PrivacyStatus);
        Assert.DoesNotContain(
            entry.DescriptionLines,
            line =>
                line.StartsWith("Blue:", System.StringComparison.Ordinal)
                || line.StartsWith("Red:", System.StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Create_ListsEachTeamsHeroesAndBattleTags()
    {
        var loaded = new LoadedReplay
        {
            ReplayId = 42,
            Replay = new Replay
            {
                Players =
                [
                    new Player
                    {
                        Name = "Salty",
                        BattleTag = 111,
                        Team = 0,
                        Character = "Li-Ming",
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Name = "Already#9",
                        BattleTag = 999,
                        Team = 0,
                        Character = "Johanna",
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Name = "Attr",
                        BattleTag = 4,
                        Team = 0,
                        HeroAttributeId = "HeroLiMing",
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Name = "Watcher",
                        BattleTag = 1,
                        Team = 2,
                        Character = "Abathur",
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Name = "Elite",
                        BattleTag = 55,
                        Team = 1,
                        Character = "Lunara",
                        PlayerType = PlayerType.Computer,
                    },
                    new Player
                    {
                        Name = "Plain",
                        BattleTag = 0,
                        Team = 1,
                        Character = "Muradin",
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Name = "NoHero",
                        BattleTag = 50,
                        Team = 1,
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Team = 1,
                        Character = "Illidan",
                        PlayerType = PlayerType.Human,
                    },
                    new Player { Team = 0, PlayerType = PlayerType.Human },
                ],
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(loaded, new YouTubeSettings());

        Assert.Contains(
            "Blue: Li-Ming (Salty#111), Johanna (Already#9), HeroLiMing (Attr#4)",
            entry.DescriptionLines
        );
        Assert.Contains(
            "Red: Lunara (AI), Muradin (Plain), NoHero#50, Illidan",
            entry.DescriptionLines
        );
        Assert.DoesNotContain(
            entry.DescriptionLines,
            line =>
                line.Contains("Abathur", System.StringComparison.Ordinal)
                || line.Contains("#55", System.StringComparison.Ordinal)
                || line.Contains("#0", System.StringComparison.Ordinal)
        );
        int roster = System.Array.FindIndex(
            entry.DescriptionLines,
            line => line.StartsWith("Blue:", System.StringComparison.Ordinal)
        );
        int tags = System.Array.IndexOf(
            entry.DescriptionLines,
            "Hashtags: #HeroesOfTheStorm #SaltySadism"
        );
        Assert.Equal("Twitch: http://twitch.tv/saltysadism", entry.DescriptionLines[0]);
        Assert.True(roster > 0);
        Assert.True(tags > roster);
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

        Assert.Equal("Dragon Shire", entry.Map);
        Assert.Equal("Platinum", entry.Rank);
        Assert.Null(entry.GameType);
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
