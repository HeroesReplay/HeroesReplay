using System;
using System.IO;
using Heroes.ReplayParser;
using HeroesReplay.Core.Configuration;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Replays;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Playlists;
using HeroesReplay.Core.YouTube.Search;
using Microsoft.Extensions.Configuration;
using Xunit;
using static HeroesReplay.Tests.Unit.YouTube.Metadata.StatHookPickerTests;

namespace HeroesReplay.Tests.Unit.YouTube.Metadata;

/// <summary>How a statistics hook reaches the title and description (issue #272).</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public sealed class StatHookTitleTests : IDisposable
{
    private readonly string data = Path.Combine(
        Path.GetTempPath(),
        "heroesreplay-stathooks-" + Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(data))
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void Hook_TakesTheDraftSlotAndTheAttributionLeadsTheDescription()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            Match(),
            Options(enabled: true)
        );

        Assert.Equal(
            "Garden of Terror - Storm League - Diamond 2 - Valla + Whitemane duo - 65644212",
            metadata.Title
        );
        Assert.Equal("Valla + Whitemane duo", metadata.StatHook);
        Assert.Equal(StatHookPicker.Attribution, metadata.DescriptionLines[0]);
        Assert.Equal("Full match.", metadata.DescriptionLines[1]);
        Assert.Contains(
            "Stats: Valla and Whitemane won 56.7% of 577 games together (Storm League, patch 2.57).",
            metadata.DescriptionLines
        );
        Assert.True(metadata.ClaimsFullMatch);
        Assert.Equal("7", metadata.TemplateVersion);
    }

    [Fact]
    public void Hook_KeepsTheTitleReadableByTheLibraryAndTheReplayIdReader()
    {
        FullMatchMetadata hooked = FullMatchMetadataBuilder.Build(Match(), Options(enabled: true));
        FullMatchMetadata plain = FullMatchMetadataBuilder.Build(Match(), Options(enabled: false));

        YouTubeLibraryVideo fromHooked = YouTubeVideoFacts.Read(
            "v1",
            hooked.Title,
            hooked.Description,
            "public"
        );
        YouTubeLibraryVideo fromTitleOnly = YouTubeVideoFacts.Read(
            "v1",
            hooked.Title,
            null,
            "public"
        );
        YouTubeLibraryVideo fromPlain = YouTubeVideoFacts.Read(
            "v2",
            plain.Title,
            plain.Description,
            "public"
        );

        Assert.True(YouTubeReplayMatch.TryReadTitleId(hooked.Title, out int id));
        Assert.Equal(65644212, id);
        foreach (YouTubeLibraryVideo video in new[] { fromHooked, fromTitleOnly })
        {
            Assert.Equal(fromPlain.ReplayId, video.ReplayId);
            Assert.Equal(fromPlain.Map, video.Map);
            Assert.Equal(fromPlain.Mode, video.Mode);
            Assert.Equal(fromPlain.Rank, video.Rank);
            Assert.Null(video.Draft);
            Assert.Null(video.FocusHero);
        }

        Assert.Equal("Garden of Terror", fromHooked.Map);
        Assert.Equal("Diamond 2", fromHooked.Rank);
    }

    [Fact]
    public void Off_LeavesTheTitleAndDescriptionAsBefore()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            Match(),
            Options(enabled: false)
        );
        FullMatchMetadata noStats = FullMatchMetadataBuilder.Build(
            Match() with
            {
                HeroStats = null,
            },
            Options(enabled: true)
        );

        foreach (FullMatchMetadata plain in new[] { metadata, noStats })
        {
            Assert.Equal("Garden of Terror - Storm League - Diamond 2 - 65644212", plain.Title);
            Assert.Null(plain.StatHook);
            Assert.Equal("Full match.", plain.DescriptionLines[0]);
            Assert.DoesNotContain(
                plain.DescriptionLines,
                line => line.StartsWith("Stats:", StringComparison.Ordinal)
            );
            Assert.DoesNotContain(StatHookPicker.Attribution, plain.DescriptionLines);
        }
    }

    [Fact]
    public void DraftNote_WinsTheSlot()
    {
        var catalog = new[]
        {
            new Hero("Johanna", "HeroJohanna", "Johanna", "Johanna", role: HeroDraft.Tank),
            new Hero("Muradin", "HeroMuradin", "Muradin", "Muradin", role: HeroDraft.Tank),
            new Hero("Rehgar", "HeroRehgar", "Rehgar", "Rehgar", role: HeroDraft.Healer),
            new Hero("Valla", "HeroValla", "Valla", "Valla", role: HeroDraft.RangedAssassin),
            new Hero("Jaina", "HeroJaina", "Jaina", "Jaina", role: HeroDraft.RangedAssassin),
        };
        HeroStatsSnapshot stats = Snapshot(
            Hero("Valla", "Valla", 3629, 7226, allies: new[] { Pair("Jaina", 327, 577) }),
            Hero("Jaina", "Jaina", 5000, 10000)
        );

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 9,
                Map = "Cursed Hollow",
                GameMode = "Storm League",
                Rank = "Diamond",
                Roster = new[]
                {
                    Blue("Johanna"),
                    Blue("Muradin"),
                    Blue("Rehgar"),
                    Blue("Valla"),
                    Blue("Jaina"),
                },
                HeroCatalog = catalog,
                HeroStats = stats,
            },
            Options(enabled: true)
        );

        Assert.Contains(
            metadata.DescriptionLines,
            line => line.StartsWith("Draft:", StringComparison.Ordinal)
        );
        Assert.DoesNotContain("duo", metadata.Title, StringComparison.Ordinal);
        Assert.Null(metadata.StatHook);
        Assert.DoesNotContain(StatHookPicker.Attribution, metadata.DescriptionLines);
    }

    [Fact]
    public void LongTitle_DropsTheHookFirstAndKeepsTheReplayId()
    {
        FullMatchMetadataInput input = Match() with
        {
            Map = "Tomb of the Spider Queen",
            Rank = "Grandmaster",
            FocusHero = "Alexstrasza",
            NamedPlayer = true,
            Roster = new[]
            {
                Blue("Valla"),
                Blue("Whitemane"),
                Red("Alexstrasza"),
                Red("Xal'atath"),
            },
        };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, Options(enabled: true));

        Assert.Equal(
            "Alexstrasza focus - Ft. Xal'atath - Tomb of the Spider Queen - Storm League - Grandmaster - 65644212",
            metadata.Title
        );
        Assert.True(metadata.Title.Length <= FullMatchMetadataBuilder.TitleMaxCharacters);
        Assert.True(YouTubeReplayMatch.TryReadTitleId(metadata.Title, out int id));
        Assert.Equal(65644212, id);
    }

    [Fact]
    public void Hook_SkipsTheNamedAndFeaturedHeroes()
    {
        HeroStatsSnapshot stats = Snapshot(
            Hero(
                "Demo",
                "Valla",
                5000,
                10000,
                enemies: new[] { Pair("HXAL", 248, 400), Pair("Alex", 248, 400) }
            ),
            Hero("HXAL", "Xal'atath", 5000, 10000),
            Hero("Alex", "Alexstrasza", 5000, 10000)
        );
        FullMatchMetadataInput input = Match() with
        {
            HeroStats = stats,
            FocusHero = "Alexstrasza",
            NamedPlayer = true,
            Roster = new[] { Blue("Valla"), Red("Alexstrasza"), Red("Xal'atath") },
        };

        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(input, Options(enabled: true));

        Assert.Null(metadata.StatHook);
        Assert.Equal(
            "Alexstrasza focus - Ft. Xal'atath - Garden of Terror - Storm League - Diamond 2 - 65644212",
            metadata.Title
        );
    }

    [Fact]
    public void Entry_PutsTheAttributionOnTheSecondLineAfterTwitch()
    {
        LoadedReplay loaded = Loaded();
        var youtube = new YouTubeSettings
        {
            Titles = new YouTubeTitleSettings
            {
                StatHooks = new StatHookSettings { Enabled = true },
            },
        };

        YouTubeEntry entry = YouTubeEntryBuilder.Create(loaded, youtube, true, null, DuoStats());

        Assert.Equal(
            "Garden of Terror - Storm League - Diamond 2 - Valla + Whitemane duo - 65644212",
            entry.Title
        );
        Assert.StartsWith("Twitch: ", entry.DescriptionLines[0], StringComparison.Ordinal);
        Assert.Equal(StatHookPicker.Attribution, entry.DescriptionLines[1]);
        Assert.Contains(
            "https://www.heroesprofile.com/",
            entry.DescriptionLines[1],
            StringComparison.Ordinal
        );
        Assert.Contains(
            "Data provided by Heroes Profile",
            entry.DescriptionLines[1],
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Source_ReadsTheReplaysPatchFileOnlyWhenOnAndFresh()
    {
        new HeroStatsStore(data).Write(
            new HeroStatsSnapshot
            {
                Patch = "2.57",
                GameType = "sl",
                FetchedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                Heroes = { Hero("Demo", "Valla", 1, 2) },
            }
        );
        LoadedReplay loaded = Loaded();

        Assert.NotNull(StatHookSource.For(Settings(true), loaded, DateTimeOffset.UtcNow));
        Assert.Null(StatHookSource.For(Settings(false), loaded, DateTimeOffset.UtcNow));
        Assert.Null(StatHookSource.For(Settings(true), loaded, DateTimeOffset.UtcNow.AddDays(3)));
        loaded.HeroesProfileReplay.GameVersion = "2.55.17.98025";
        Assert.Null(StatHookSource.For(Settings(true), loaded, DateTimeOffset.UtcNow));
        Assert.Null(StatHookSource.For(Settings(true), null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AppSettings_ShipStatHooksOffEverywhere()
    {
        string basePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        IConfigurationRoot baseConfig = new ConfigurationBuilder().AddJsonFile(basePath).Build();
        StatHookSettings hooks = baseConfig
            .GetSection("YouTube:Titles:StatHooks")
            .Get<StatHookSettings>();
        HeroStatsSettings heroStats = baseConfig
            .GetSection("HeroesProfileApi:HeroStats")
            .Get<HeroStatsSettings>();

        Assert.False(hooks.Enabled);
        Assert.True(
            hooks.Counters
                && hooks.Duos
                && hooks.BestMap
                && hooks.SlipsTheBan
                && hooks.WorstMap
                && hooks.PatchExtremes
        );
        Assert.Equal(250, hooks.CounterMinGames);
        Assert.Equal(57, hooks.CounterMinWinRate);
        Assert.Equal(4, hooks.CounterMinEdge);
        Assert.Equal(300, hooks.DuoMinGames);
        Assert.Equal(56, hooks.DuoMinWinRate);
        Assert.Equal(52, hooks.DuoMinLowerBound);
        Assert.Equal(150, hooks.MapMinGames);
        Assert.Equal(3, hooks.MapMinDelta);
        Assert.Equal(40, hooks.BanMinRate);
        Assert.Equal(500, hooks.ExtremesMinGames);
        Assert.Equal(3, hooks.ExtremesCount);
        Assert.Equal(new[] { "Storm League" }, heroStats.GameTypes);
        Assert.Equal(TimeSpan.FromHours(24), heroStats.RefreshInterval);
        Assert.Equal(TimeSpan.FromHours(72), heroStats.MaxAge);
        Assert.Equal(TimeSpan.FromSeconds(61), heroStats.GroupByMapSpacing);

        foreach (string overlay in new[] { "appsettings.dev.json", "appsettings.prod.json" })
        {
            IConfigurationRoot merged = new ConfigurationBuilder()
                .AddJsonFile(basePath)
                .AddJsonFile(FindRepoFile(Path.Combine("src", "HeroesReplay.CLI", overlay)))
                .Build();
            Assert.False(merged.GetValue<bool>("YouTube:Titles:StatHooks:Enabled"), overlay);
            Assert.NotNull(merged.GetValue<string>("YouTube:Titles:StatHooks:Enabled"));
        }
    }

    private AppSettings Settings(bool enabled) =>
        new AppSettings
        {
            Location = new LocationSettings { DataDirectory = data },
            HeroesProfileApi = new HeroesProfileApiSettings(),
            YouTube = new YouTubeSettings
            {
                Titles = new YouTubeTitleSettings
                {
                    StatHooks = new StatHookSettings { Enabled = enabled },
                },
            },
        };

    private static FullMatchMetadataOptions Options(bool enabled) =>
        new FullMatchMetadataOptions
        {
            Titles = new YouTubeTitleSettings
            {
                StatHooks = new StatHookSettings { Enabled = enabled },
            },
        };

    private static HeroStatsSnapshot DuoStats() =>
        Snapshot(
            Hero("Demo", "Valla", 3629, 7226, allies: new[] { Pair("WHIT", 327, 577) }),
            Hero("WHIT", "Whitemane", 5000, 10000),
            Hero("Alex", "Alexstrasza", 5000, 10000),
            Hero("HXAL", "Xal'atath", 5000, 10000)
        );

    private static FullMatchMetadataInput Match() =>
        new FullMatchMetadataInput
        {
            ReplayId = 65644212,
            Map = "Garden of Terror",
            GameMode = "Storm League",
            Rank = "Diamond 2",
            GameVersion = "2.57.0.98348",
            Roster = new ReplayMediaPlayer[]
            {
                Blue("Valla"),
                Blue("Whitemane"),
                Red("Alexstrasza"),
                Red("Johanna"),
            },
            HeroStats = DuoStats(),
            IsCompleteRecording = true,
        };

    private static LoadedReplay Loaded() =>
        new LoadedReplay
        {
            ReplayId = 65644212,
            Replay = new Replay
            {
                Map = "Garden of Terror",
                Players = new[]
                {
                    new Player
                    {
                        Team = 0,
                        Character = "Valla",
                        Name = "One",
                        BattleTag = 1,
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Team = 0,
                        Character = "Whitemane",
                        Name = "Two",
                        BattleTag = 2,
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Team = 1,
                        Character = "Alexstrasza",
                        Name = "Three",
                        BattleTag = 3,
                        PlayerType = PlayerType.Human,
                    },
                    new Player
                    {
                        Team = 1,
                        Character = "Johanna",
                        Name = "Four",
                        BattleTag = 4,
                        PlayerType = PlayerType.Human,
                    },
                },
            },
            HeroesProfileReplay = new HeroesProfileReplay
            {
                Id = 65644212,
                Map = "Garden of Terror",
                GameType = "Storm League",
                Rank = "Diamond 2",
                GameVersion = "2.57.0.98348",
            },
        };

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
