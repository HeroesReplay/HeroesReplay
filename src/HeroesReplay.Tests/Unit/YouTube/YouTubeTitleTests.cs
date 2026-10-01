using System;
using System.IO;
using System.Text.Json;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Data;
using HeroesReplay.Core.Services.Media;
using HeroesReplay.Core.Services.YouTube;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeTitleTests
{
    private static readonly DateTime Played = new DateTime(2026, 10, 1, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_FeaturesANewHeroParsedFromTheReplay()
    {
        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            ReplayWith("Xal'atath"),
            new YouTubeSettings()
        );

        Assert.Equal(
            "Ft. Xal'atath - Volskaya Foundry - Storm League - Diamond - 65550001",
            entry.Title
        );
        Assert.Contains(entry.DescriptionLines, line => line == "Featuring: Xal'atath");
        Assert.Contains("Xal'atath", entry.Heroes);
        Assert.Contains("Johanna", entry.Heroes);
    }

    [Fact]
    public void Create_LeavesAnEstablishedHeroOutOfTheTitle()
    {
        YouTubeEntry entry = YouTubeEntryBuilder.Create(
            ReplayWith("Johanna"),
            new YouTubeSettings()
        );

        Assert.Equal("Volskaya Foundry - Storm League - Diamond - 65550001", entry.Title);
        Assert.DoesNotContain(entry.DescriptionLines, line => line.StartsWith("Featuring:"));
    }

    [Fact]
    public void FeatureNewHeroes_CanBeTurnedOff()
    {
        var settings = new YouTubeSettings
        {
            Titles = new YouTubeTitleSettings { FeatureNewHeroes = false },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(ReplayWith("Xal'atath"), settings);

        Assert.Equal("Volskaya Foundry - Storm League - Diamond - 65550001", entry.Title);
        Assert.DoesNotContain("Ft.", entry.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedPlayer_DoesNotRepeatTheFeaturedHero()
    {
        LoadedReplay loaded = ReplayWith("Xal'atath");
        loaded.RewardQueueItem = new RewardQueueItem
        {
            Request = new RewardRequest
            {
                ReplayId = 65550001,
                PlayerIndex = 1,
                Login = "ViewerZZ",
                RecordAndUpload = true,
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(loaded, new YouTubeSettings());

        Assert.Equal(
            "Xal'atath requested by ViewerZZ - Volskaya Foundry - Storm League - Diamond - 65550001",
            entry.Title
        );
        Assert.DoesNotContain("Ft.", entry.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void NamedPlayer_KeepsADifferentNewHero()
    {
        LoadedReplay loaded = ReplayWith("Illidan", "Xal'atath");
        loaded.RewardQueueItem = new RewardQueueItem
        {
            Request = new RewardRequest
            {
                ReplayId = 65550001,
                PlayerIndex = 1,
                Login = "ViewerZZ",
                RecordAndUpload = true,
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(loaded, new YouTubeSettings());

        Assert.Equal(
            "Illidan requested by ViewerZZ - Ft. Xal'atath - Volskaya Foundry - Storm League - Diamond - 65550001",
            entry.Title
        );
    }

    [Fact]
    public void RequestedByTitles_CanBeTurnedOff()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 7,
                Map = "Dragon Shire",
                GameMode = "Storm League",
                Rank = "Diamond",
                FocusHero = "Illidan",
                NamedPlayer = true,
                RecordAndUpload = true,
                RequestedBy = "ViewerZZ",
            },
            new FullMatchMetadataOptions
            {
                Titles = new YouTubeTitleSettings { RequestedByTitles = false },
            }
        );

        Assert.Equal("Illidan - Dragon Shire - Storm League - Diamond - 7", metadata.Title);
    }

    [Fact]
    public void ReleaseWindow_FeaturesTheNewestHeroAndIgnoresAnOldOne()
    {
        var titles = new YouTubeTitleSettings { RecentHeroes = Array.Empty<string>() };
        Hero older = Released("Older", new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc));
        Hero newer = Released("Newer", new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc));
        Hero ancient = Released("Johanna", new DateTime(2014, 3, 13, 0, 0, 0, DateTimeKind.Utc));

        FullMatchMetadata featured = FullMatchMetadataBuilder.Build(
            Match(Played, "Older", "Newer", "Johanna") with
            {
                HeroCatalog = new[] { older, newer, ancient },
            },
            new FullMatchMetadataOptions { Titles = titles }
        );

        Assert.Equal("Ft. Newer - Dragon Shire - Storm League - Diamond - 42", featured.Title);
        Assert.Contains("Featuring: Newer", featured.DescriptionLines);

        FullMatchMetadata quiet = FullMatchMetadataBuilder.Build(
            Match(Played, "Johanna") with
            {
                HeroCatalog = new[] { ancient },
            },
            new FullMatchMetadataOptions { Titles = titles }
        );
        Assert.Equal("Dragon Shire - Storm League - Diamond - 42", quiet.Title);

        FullMatchMetadata closed = FullMatchMetadataBuilder.Build(
            Match(Played, "Newer") with
            {
                HeroCatalog = new[] { newer },
            },
            new FullMatchMetadataOptions
            {
                Titles = new YouTubeTitleSettings
                {
                    RecentHeroes = Array.Empty<string>(),
                    RecentHeroDays = 0,
                },
            }
        );
        Assert.DoesNotContain("Ft.", closed.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfiguredSpelling_IsUsedWhenTheReplayOmitsTheApostrophe()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            Match(Played, "Xalatath"),
            new FullMatchMetadataOptions
            {
                Titles = new YouTubeTitleSettings { RecentHeroes = new[] { "Xal'atath" } },
            }
        );

        Assert.StartsWith("Ft. Xal'atath - Dragon Shire", metadata.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftNotes_CanBeTurnedOff()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 9,
                Map = "Cursed Hollow",
                GameMode = "Storm League",
                Rank = "Diamond",
                Roster = new[]
                {
                    Player(0, "Johanna"),
                    Player(0, "Muradin"),
                    Player(0, "Rehgar"),
                    Player(0, "Valla"),
                    Player(0, "Jaina"),
                },
                HeroCatalog = new[]
                {
                    Released("Johanna", null, HeroDraft.Tank),
                    Released("Muradin", null, HeroDraft.Tank),
                    Released("Rehgar", null, HeroDraft.Healer),
                    Released("Valla", null, HeroDraft.RangedAssassin),
                    Released("Jaina", null, HeroDraft.RangedAssassin),
                },
            },
            new FullMatchMetadataOptions
            {
                Titles = new YouTubeTitleSettings { DraftNotes = false },
            }
        );

        Assert.Equal("Cursed Hollow - Storm League - Diamond - 9", metadata.Title);
        Assert.DoesNotContain(metadata.DescriptionLines, line => line.StartsWith("Draft:"));
    }

    [Fact]
    public void ReleaseDate_ReadsTheCatalogDay()
    {
        using JsonDocument dated = JsonDocument.Parse("{\"releaseDate\":\"2026-09-28\"}");
        using JsonDocument blank = JsonDocument.Parse("{\"releaseDate\":\"soon\"}");
        using JsonDocument missing = JsonDocument.Parse("{}");

        Assert.Equal(
            new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
            GameData.ReadReleaseDate(dated.RootElement)
        );
        Assert.Null(GameData.ReadReleaseDate(blank.RootElement));
        Assert.Null(GameData.ReadReleaseDate(missing.RootElement));
    }

    [Fact]
    public void AppSettings_BindsTitleRulesAndTheNewHero()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        string text = File.ReadAllText(path);
        YouTubeSettings youtube = new ConfigurationBuilder()
            .AddJsonFile(path)
            .Build()
            .GetSection("YouTube")
            .Get<YouTubeSettings>();

        Assert.Contains("\"FeaturePrefix\": \"Ft.\"", text, StringComparison.Ordinal);
        Assert.Contains("\"Xal'atath\"", text, StringComparison.Ordinal);
        Assert.True(youtube.Titles.DraftNotes);
        Assert.True(youtube.Titles.NamedPlayerTitles);
        Assert.True(youtube.Titles.RequestedByTitles);
        Assert.True(youtube.Titles.FeatureNewHeroes);
        Assert.Equal("Ft.", youtube.Titles.FeaturePrefix);
        Assert.Equal(60, youtube.Titles.RecentHeroDays);
        Assert.Equal(new[] { "Xal'atath" }, youtube.Titles.RecentHeroes);
        Assert.True(youtube.Titles.NoRangedAssassin);
        Assert.Equal("Ranged Assassin", youtube.Titles.RangedAssassin);
        Assert.Equal("Bruiser", youtube.Titles.Bruiser);

        string prod = FindRepoFile(
            Path.Combine("src", "HeroesReplay.CLI", "appsettings.prod.json")
        );
        string prodText = File.ReadAllText(prod);
        Assert.Contains("\"Xal'atath\"", prodText, StringComparison.Ordinal);
        Assert.Contains("\"BeforeNextReplay\": \"00:01:30\"", prodText, StringComparison.Ordinal);
    }

    private static LoadedReplay ReplayWith(params string[] heroes)
    {
        var players = new Player[heroes.Length + 1];
        players[0] = new Player
        {
            Team = 0,
            Character = "Johanna",
            PlayerType = PlayerType.Human,
        };
        for (int i = 0; i < heroes.Length; i++)
        {
            players[i + 1] = new Player
            {
                Team = 1,
                Character = heroes[i],
                PlayerType = PlayerType.Human,
            };
        }

        return new LoadedReplay
        {
            ReplayId = 65550001,
            Replay = new Replay { Map = "Volskaya Foundry", Players = players },
            HeroesProfileReplay = new HeroesProfileReplay
            {
                Id = 65550001,
                Map = "Volskaya Foundry",
                GameType = "Storm League",
                Rank = "Diamond",
            },
        };
    }

    private static FullMatchMetadataInput Match(DateTime played, params string[] heroes)
    {
        var roster = new ReplayMediaPlayer[heroes.Length];
        for (int i = 0; i < heroes.Length; i++)
        {
            roster[i] = Player(i % 2, heroes[i]);
        }

        return new FullMatchMetadataInput
        {
            ReplayId = 42,
            GameDateUtc = played,
            Map = "Dragon Shire",
            GameMode = "Storm League",
            Rank = "Diamond",
            Roster = roster,
        };
    }

    private static ReplayMediaPlayer Player(int team, string hero)
    {
        return new ReplayMediaPlayer { Team = team, Hero = hero };
    }

    private static Hero Released(string name, DateTime? released, string role = null)
    {
        return new Hero(
            name,
            "Hero" + name,
            name,
            name,
            role: role ?? HeroDraft.RangedAssassin,
            releaseDate: released
        );
    }

    private static string FindRepoFile(string relative)
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(relative);
    }
}
