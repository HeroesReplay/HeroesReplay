using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.MediaPolicy;
using HeroesReplay.Core.Shared;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Metadata;
using HeroesReplay.Core.YouTube.Playlists;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Playlists;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeLibraryPlannerTests
{
    private const string Line = "2.57.0.98304";

    private static readonly DateTimeOffset Noon = new(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Titles_RankedStormLeagueGoesIntoMapModeAndRank()
    {
        IReadOnlyList<string> titles = Titles(
            Video("v", "Alterac Pass", "Storm League", "Diamond 2")
        );

        Assert.Equal(new[] { "Alterac Pass", "Storm League", "Storm League - Diamond" }, titles);
    }

    [Fact]
    public void Titles_UnrankedStormLeagueHasNoRankPlaylist()
    {
        Assert.Equal(
            new[] { "Cursed Hollow", "Storm League" },
            Titles(Video("v", "Cursed Hollow", "Storm League", null))
        );
    }

    [Fact]
    public void Titles_QuickMatchAndAramHaveNoRankPlaylist()
    {
        Assert.Equal(
            new[] { "Sky Temple", "Quick Match" },
            Titles(Video("qm", "Sky Temple", "Quick Match", "Gold 2"))
        );
        Assert.Equal(
            new[] { "Braxis Outpost", "ARAM" },
            Titles(Video("aram", "Braxis Outpost", "ARAM", null))
        );
    }

    [Theory]
    [InlineData("No healer")]
    [InlineData("Blue no tank")]
    [InlineData("Blue no tank, Red double healer")]
    [InlineData("Red no tank or healer")]
    [InlineData("Blue triple bruiser, Red 4 healers")]
    [InlineData("Blue double bruiser, Red dive, Split push")]
    public void Titles_UnusualDraftGoesIntoTheOneUnusualDraftsPlaylist(string draft)
    {
        YouTubeLibraryVideo video = Video("v", "Dragon Shire", "Storm League", "Master");
        video.Draft = draft;

        IReadOnlyList<string> titles = Titles(video);

        Assert.Equal(
            new[] { YouTubePlaylistNames.UnusualDrafts },
            titles.Where(title => title.StartsWith("Unusual drafts"))
        );
    }

    [Fact]
    public void Titles_UsualDraftHasNoDraftPlaylist()
    {
        YouTubeLibraryVideo video = Video("v", "Dragon Shire", "Storm League", "Master");
        video.Draft = " ";

        Assert.DoesNotContain(YouTubePlaylistNames.UnusualDrafts, Titles(video));
    }

    [Fact]
    public void Titles_ViewerReviewRequestGoesIntoTheReviewPlaylist()
    {
        YouTubeLibraryVideo video = Video("v", "Dragon Shire", "Storm League", "Diamond 3");
        video.FocusHero = "Illidan";

        Assert.Contains(YouTubePlaylistNames.ViewerReviews, Titles(video));
        Assert.True(video.IsViewerReview);
    }

    [Fact]
    public void Titles_OldTemplateVideoIsFiledByMapModeAndRankOnly()
    {
        YouTubeLibraryVideo video = YouTubeVideoFacts.Read(
            "old",
            "Sky Temple - 65269475 - Platinum",
            "Twitch: https://twitch.tv/saltysadism\r\nHeroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID=65269475\r\nGame type: Storm League\r\nRank: Platinum 1",
            "public"
        );
        YouTubeVideoFacts.Fill(
            video,
            new HeroesProfileReplay { Map = "Sky Temple", GameVersion = "2.55.14.95918" }
        );

        Assert.True(video.IsResolved);
        Assert.Null(video.Draft);
        Assert.Null(video.FocusHero);
        Assert.Equal(
            new[] { "Sky Temple", "Storm League", "Storm League - Platinum" },
            Titles(video)
        );
    }

    [Fact]
    public void Titles_ClipGoesIntoNoPlaylist()
    {
        var clip = new YouTubeLibraryVideo
        {
            VideoId = "clip",
            Kind = YouTubeLibraryRecord.Clip,
            Map = "Alterac Pass",
            GameVersion = Line,
            PrivacyStatus = "public",
            FocusHero = "Li-Ming",
        };

        Assert.Empty(Titles(clip));
    }

    [Fact]
    public void Titles_UnfiledModeGoesIntoNoPlaylist()
    {
        YouTubeLibraryVideo video = Video("v", "Alterac Pass", "Custom", null);
        video.Draft = "No healer";

        Assert.Empty(Titles(video));
    }

    [Fact]
    public void Titles_FollowTheGroupSwitches()
    {
        YouTubeLibraryVideo video = Video("v", "Alterac Pass", "Storm League", "Diamond 2");
        video.Draft = "Double tank";
        video.FocusHero = "Muradin";

        Assert.Empty(Titles(video, Groups(false)));
        Assert.Equal(new[] { "Alterac Pass" }, Titles(video, Only(groups => groups.Map = true)));
        Assert.Equal(new[] { "Storm League" }, Titles(video, Only(groups => groups.Mode = true)));
        Assert.Equal(
            new[] { "Storm League - Diamond" },
            Titles(video, Only(groups => groups.Rank = true))
        );
        Assert.Equal(
            new[] { YouTubePlaylistNames.UnusualDrafts },
            Titles(video, Only(groups => groups.Draft = true))
        );
        Assert.Equal(
            new[] { YouTubePlaylistNames.ViewerReviews },
            Titles(video, Only(groups => groups.ViewerReview = true))
        );
        Assert.Equal(
            new[] { "Alterac Pass - Storm League - Diamond" },
            Titles(video, Only(groups => groups.MapMode = true))
        );
    }

    [Fact]
    public void Defaults_FileEveryNewGroupAndNotTheCombinedMapModePlaylist()
    {
        var groups = new YouTubePlaylistSettings();

        Assert.True(groups.Map);
        Assert.True(groups.Mode);
        Assert.True(groups.Rank);
        Assert.True(groups.Draft);
        Assert.True(groups.ViewerReview);
        Assert.True(groups.HeroFocus);
        Assert.False(groups.HeroFocusRole);
        Assert.False(groups.MapMode);
        Assert.NotNull(new YouTubeSettings().Playlists);
    }

    [Fact]
    public void AppSettings_ListsEveryGroupWithTheDefaults()
    {
        YouTubeSettings youtube = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            .Build()
            .GetSection("YouTube")
            .Get<YouTubeSettings>();

        YouTubePlaylistSettings groups = youtube.Playlists;
        Assert.True(groups.Map && groups.Mode && groups.Rank && groups.Draft);
        Assert.True(groups.ViewerReview && groups.HeroFocus);
        Assert.False(groups.HeroFocusRole);
        Assert.False(groups.MapMode);
    }

    [Fact]
    public void Plan_FilesEachVideoIntoEachPlaylistOnce()
    {
        YouTubeLibraryVideo first = Video("v-1", "Alterac Pass", "Storm League", "Gold");
        first.Draft = "Blue no tank, Red no tank";
        YouTubeLibraryVideo again = Video("v-1", "Sky Temple", "ARAM", null);

        IReadOnlyList<YouTubeLibraryItem> items = YouTubeLibraryPlanner.Plan(
            new[] { first, again },
            Groups(true)
        );

        Assert.Equal(
            items.Count,
            items.Select(item => item.PlaylistTitle + "\n" + item.VideoId).Distinct().Count()
        );
        Assert.All(items, item => Assert.Equal("v-1", item.VideoId));
        Assert.DoesNotContain(items, item => item.PlaylistTitle == "Sky Temple");
        Assert.Single(items, item => item.PlaylistTitle == YouTubePlaylistNames.UnusualDrafts);
        Assert.Single(items, item => item.PlaylistTitle == "Storm League");
    }

    [Fact]
    public void Plan_SkipsPrivateVideosAndFilesTheNewestUploadFirst()
    {
        YouTubeLibraryVideo old = Video("old", "Sky Temple", "Storm League", "Gold");
        old.UploadedAt = Noon.AddDays(-30);
        YouTubeLibraryVideo fresh = Video("fresh", "Cursed Hollow", "Storm League", "Gold");
        fresh.UploadedAt = Noon;
        YouTubeLibraryVideo undated = Video("undated", "Dragon Shire", "Quick Match", null);
        YouTubeLibraryVideo hidden = Video("hidden", "Dragon Shire", "Storm League", "Gold");
        hidden.PrivacyStatus = "private";

        IReadOnlyList<YouTubeLibraryItem> items = YouTubeLibraryPlanner.Plan(
            new[] { undated, old, hidden, fresh },
            Groups(true)
        );

        Assert.Equal(
            new[] { "fresh", "old", "undated" },
            items.Select(item => item.VideoId).Distinct()
        );
        Assert.DoesNotContain(items, item => item.VideoId == "hidden");
    }

    [Fact]
    public void Plan_FilesAPrivateStagingEntryOnceYouTubeReportsItPublic()
    {
        YouTubeEntry staged = Entry("staged", "private", "2.57.0.98304");
        staged.ActualPrivacyStatus = "public";
        YouTubeEntry test = Entry("test", "private", "2.57.0.98304");

        YouTubeLibraryItem item = Assert.Single(
            YouTubeLibraryPlanner.Plan(
                new[] { staged, test }.Select(entry => YouTubeLibraryRecord.FromEntry(entry, Noon)),
                Only(groups => groups.Map = true)
            )
        );

        Assert.Equal("Alterac Pass", item.PlaylistTitle);
        Assert.Equal("staged", item.VideoId);
    }

    [Fact]
    public void Titles_AreStableAndWithinYouTubesLimit()
    {
        string[] maps =
        {
            "Alterac Pass",
            "Battlefield of Eternity",
            "Blackheart's Bay",
            "Tomb of the Spider Queen",
            "Braxis Outpost",
        };
        string[] ranks = { "Bronze 5", "Grand Master", "Master", null };
        string[] drafts =
        {
            "No tank or healer",
            "Blue double bruiser, Red triple healer",
            "Red " + new string('x', 400),
        };
        foreach (string map in maps)
        foreach (string rank in ranks)
        foreach (string draft in drafts)
        {
            YouTubeLibraryVideo video = Video("v", map, "Storm League", rank);
            video.Draft = draft;
            video.FocusHero = "Illidan";

            IReadOnlyList<string> first = YouTubeLibraryPlanner.Titles(video, Groups(true));
            IReadOnlyList<string> second = YouTubeLibraryPlanner.Titles(video, Groups(true));

            Assert.Equal(first, second);
            Assert.All(
                first,
                title =>
                {
                    Assert.InRange(title.Length, 1, YouTubePlaylistNames.MaxTitleLength);
                    Assert.Equal(title.Trim(), title);
                }
            );
        }
    }

    [Fact]
    public void Record_FromAnInsertedEntryKeepsTheDraftNoteAndTheNamedPlayer()
    {
        FullMatchMetadata metadata = FullMatchMetadataBuilder.Build(
            new FullMatchMetadataInput
            {
                ReplayId = 65550001,
                GameVersion = Line,
                Map = "Dragon Shire",
                GameMode = "Storm League",
                Rank = "Diamond 3",
                FocusHero = "Illidan",
                NamedPlayer = true,
                RecordAndUpload = true,
                RequestedBy = "viewer",
                HeroCatalog = Catalog,
                Roster = Roster(
                    blue: new[] { "Chen", "Rehgar", "Raynor", "Valla", "Jaina" },
                    red: new[] { "Johanna", "Leoric", "Uther", "Valla", "Illidan" }
                ),
            },
            new FullMatchMetadataOptions()
        );
        var lines = new List<string> { "Twitch: https://twitch.tv/saltysadism" };
        lines.AddRange(metadata.DescriptionLines);
        var entry = new YouTubeEntry
        {
            VideoId = "new-1",
            ReplayId = 65550001,
            Title = metadata.Title,
            Map = metadata.Map,
            GameType = metadata.GameMode,
            Rank = metadata.Rank,
            GameVersion = Line,
            PrivacyStatus = "public",
            DescriptionLines = lines.ToArray(),
        };

        YouTubeLibraryVideo inserted = YouTubeLibraryRecord.FromEntry(entry, Noon);
        YouTubeLibraryVideo listed = YouTubeVideoFacts.Read(
            "new-1",
            metadata.Title,
            string.Join("\n", lines),
            "public"
        );

        Assert.Equal("Blue no tank", inserted.Draft);
        Assert.Equal("Illidan", inserted.FocusHero);
        Assert.Equal(inserted.Draft, listed.Draft);
        Assert.Equal(inserted.FocusHero, listed.FocusHero);
        Assert.Equal(
            new[]
            {
                "Dragon Shire",
                "Storm League",
                "Storm League - Diamond",
                YouTubePlaylistNames.UnusualDrafts,
                YouTubePlaylistNames.ViewerReviews,
            },
            Titles(inserted)
        );
        Assert.Equal(Titles(inserted), Titles(listed));
    }

    [Fact]
    public void Record_ARequestWithoutAPlayerIsNotAReview()
    {
        var entry = new YouTubeEntry
        {
            VideoId = "paid",
            Map = "Dragon Shire",
            GameType = "Storm League",
            Requested = true,
            DescriptionLines = new[] { "Full match.", "Requested by: viewer" },
        };

        YouTubeLibraryVideo video = YouTubeLibraryRecord.FromEntry(entry, Noon);

        Assert.Null(video.FocusHero);
        Assert.False(video.IsViewerReview);
    }

    private static readonly IReadOnlyList<Hero> Catalog = new[]
    {
        Hero("Johanna", HeroDraft.Tank),
        Hero("Chen", HeroDraft.Bruiser),
        Hero("Leoric", HeroDraft.Bruiser),
        Hero("Rehgar", HeroDraft.Healer),
        Hero("Uther", HeroDraft.Healer),
        Hero("Valla", HeroDraft.RangedAssassin),
        Hero("Jaina", HeroDraft.RangedAssassin),
        Hero("Raynor", HeroDraft.RangedAssassin),
        Hero("Illidan", HeroDraft.MeleeAssassin),
    };

    private static IReadOnlyList<string> Titles(
        YouTubeLibraryVideo video,
        YouTubePlaylistSettings groups = null
    ) => YouTubeLibraryPlanner.Titles(video, groups ?? new YouTubePlaylistSettings());

    private static YouTubeLibraryVideo Video(string id, string map, string mode, string rank) =>
        new()
        {
            VideoId = id,
            ReplayId = 1,
            Kind = YouTubeLibraryRecord.Full,
            Map = map,
            Mode = mode,
            Rank = rank,
            GameVersion = Line,
            PrivacyStatus = "public",
        };

    private static YouTubeEntry Entry(string id, string privacy, string build) =>
        new()
        {
            VideoId = id,
            PrivacyStatus = privacy,
            GameVersion = build,
            ReplayId = 1,
            Map = "Alterac Pass",
            GameType = "Storm League",
        };

    private static YouTubePlaylistSettings Groups(bool on) =>
        new()
        {
            Map = on,
            Mode = on,
            Rank = on,
            Draft = on,
            ViewerReview = on,
            MapMode = on,
        };

    private static YouTubePlaylistSettings Only(Action<YouTubePlaylistSettings> turnOn)
    {
        YouTubePlaylistSettings groups = Groups(false);
        turnOn(groups);
        return groups;
    }

    private static Hero Hero(string name, string role) =>
        new(name, "Hero" + name, name, name, role: role);

    private static List<ReplayMediaPlayer> Roster(string[] blue, string[] red)
    {
        var players = new List<ReplayMediaPlayer>();
        players.AddRange(blue.Select(hero => new ReplayMediaPlayer { Team = 0, Hero = hero }));
        players.AddRange(red.Select(hero => new ReplayMediaPlayer { Team = 1, Hero = hero }));
        return players;
    }
}
