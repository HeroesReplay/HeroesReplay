using Heroes.ReplayParser;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Requests;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Metadata;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

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
        Assert.Equal("Volskaya Foundry - Storm League - Diamond - 65389750", entry.Title);
        Assert.DoesNotContain("Full match", entry.Title, System.StringComparison.Ordinal);
        Assert.DoesNotContain(
            "player priority",
            entry.Title,
            System.StringComparison.OrdinalIgnoreCase
        );
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
    public void Create_ListsEachTeamsHeroesWithoutBattleTagNumbers()
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
            "Blue: Li-Ming (Salty), Johanna (Already), HeroLiMing (Attr)",
            entry.DescriptionLines
        );
        Assert.Contains(
            "Red: Lunara (AI), Muradin (Plain), NoHero, Illidan",
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
        Assert.Equal("Twitch: https://twitch.tv/saltysadism", entry.DescriptionLines[0]);
        Assert.True(roster > 0);
        Assert.DoesNotContain(
            entry.DescriptionLines,
            line => line.Contains("Hashtags:", System.StringComparison.Ordinal)
        );
        Assert.Equal(FullMatchMetadataBuilder.TemplateVersion, entry.TemplateVersion);
    }

    [Fact]
    public void Create_CompleteRecording_SaysFullMatchAndKeepsTheTemplateVersion()
    {
        var loaded = new LoadedReplay
        {
            ReplayId = 42,
            HeroesProfileReplay = new HeroesProfileReplay { Id = 42, Map = "Cursed Hollow" },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            loaded,
            new YouTubeSettings { PrivacyStatus = "private" },
            isCompleteRecording: true
        );

        Assert.Equal(FullMatchMetadataBuilder.TemplateVersion, entry.TemplateVersion);
        Assert.Contains("Full match.", entry.DescriptionLines);
        Assert.DoesNotContain(
            entry.DescriptionLines,
            line => line.Contains('#', System.StringComparison.Ordinal)
        );
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
        Assert.Equal("Dragon Shire - Platinum - 65396086", entry.Title);
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

        Assert.Equal("Braxis Holdout - Platinum - 65396084", entry.Title);
    }

    [Fact]
    public void Create_PrefixesThePriorityHero()
    {
        var loaded = new LoadedReplay
        {
            ReplayId = 1,
            Replay = new Replay
            {
                Map = "Dragon Shire",
                Players = new[]
                {
                    new Player { Character = "Illidan" },
                    new Player { Character = "Johanna" },
                },
            },
            HeroesProfileReplay = new HeroesProfileReplay
            {
                Id = 1,
                Map = "Dragon Shire",
                GameType = "Storm League",
                Rank = "Diamond 3",
            },
            RewardQueueItem = new RewardQueueItem
            {
                Request = new RewardRequest { ReplayId = 1, PlayerIndex = 0 },
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(loaded, new YouTubeSettings());

        Assert.Equal("Illidan focus - Dragon Shire - Storm League - Diamond 3 - 1", entry.Title);
        Assert.DoesNotContain("Full match", entry.Title, System.StringComparison.Ordinal);
        Assert.DoesNotContain("MMR", entry.Title, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_NamesTheRequestedPlayerAndTheTwitchLogin()
    {
        var loaded = new LoadedReplay
        {
            ReplayId = 1,
            Replay = new Replay
            {
                Map = "Dragon Shire",
                Players = new[] { new Player { Character = "Illidan" } },
            },
            HeroesProfileReplay = new HeroesProfileReplay
            {
                Id = 1,
                Map = "Dragon Shire",
                GameType = "Storm League",
                Rank = "Diamond 3",
                AverageMmr = 2500,
            },
            RewardQueueItem = new RewardQueueItem
            {
                Request = new RewardRequest
                {
                    ReplayId = 1,
                    PlayerIndex = 0,
                    Login = "ViewerZZ",
                    RecordAndUpload = true,
                },
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(loaded, new YouTubeSettings());

        Assert.Equal("Illidan focus - Dragon Shire - Storm League - Diamond 3 - 1", entry.Title);
        Assert.DoesNotContain("MMR", entry.Title, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            entry.DescriptionLines,
            line => line.Contains("MMR", System.StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            entry.DescriptionLines,
            line => line.Contains("Requested by: ViewerZZ", System.StringComparison.Ordinal)
        );
        Assert.Contains("Illidan", entry.Heroes);
    }

    [Fact]
    public void Create_PlainReplayIdRewardIsARequestThatNamesTheViewer()
    {
        // #165: youtube-entry.json for replay 65625279 said Requested false with no requester.
        var loaded = new LoadedReplay
        {
            ReplayId = 65625279,
            Replay = new Replay { Map = "Industrial District" },
            HeroesProfileReplay = new HeroesProfileReplay
            {
                Id = 65625279,
                Map = "Industrial District",
                GameType = "ARAM",
            },
            RewardQueueItem = new RewardQueueItem
            {
                Request = new RewardRequest
                {
                    ReplayId = 65625279,
                    Login = "Zemill",
                    RewardTitle = "ReplayId",
                    RecordAndUpload = false,
                },
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(loaded, new YouTubeSettings());

        Assert.True(entry.Requested);
        Assert.Contains(
            entry.DescriptionLines,
            line => line.Contains("Requested by: Zemill", System.StringComparison.Ordinal)
        );
        Assert.DoesNotContain("Zemill", entry.Title, System.StringComparison.Ordinal);
    }
}
