using HeroesReplay.Core.HeroesProfile;
using HeroesReplay.Core.YouTube.Playlists;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Playlists;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubeVideoFactsTests
{
    [Theory]
    [InlineData("Sky Temple - 65269475 - Platinum", "Sky Temple", "Storm League", "Platinum")]
    [InlineData(
        "Volskaya Foundry - 65537358 - Storm League - Master",
        "Volskaya Foundry",
        "Storm League",
        "Master"
    )]
    [InlineData(
        "용의 둥지 - 65537359 - Storm League - Gold",
        "Dragon Shire",
        "Storm League",
        "Gold"
    )]
    [InlineData(
        "Illidan focus - Dragon Shire - Storm League - Diamond 3 - 65550001",
        "Dragon Shire",
        "Storm League",
        "Diamond 3"
    )]
    public void Read_OlderTitlesWithAGameTypeLine(
        string title,
        string map,
        string mode,
        string rank
    )
    {
        YouTubeLibraryVideo video = YouTubeVideoFacts.Read(
            "v",
            title,
            "Heroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID=65269475\r\nGame type: Storm League",
            "public"
        );

        Assert.Equal(map, video.Map);
        Assert.Equal(mode, video.Mode);
        Assert.Equal(rank, video.Rank);
        Assert.Null(video.GameVersion);
        Assert.False(video.IsResolved);
    }

    [Fact]
    public void Read_ALocalizedMapNobodyMapsIsLeftForHeroesProfile()
    {
        YouTubeLibraryVideo video = YouTubeVideoFacts.Read(
            "v",
            "Wieże Zagłady - 65537001 - Storm League - Master",
            "replayID=65537001\nGame type: Storm League",
            "public"
        );

        Assert.Null(video.Map);
        Assert.Equal(65537001, video.ReplayId);

        YouTubeVideoFacts.Fill(
            video,
            new HeroesProfileReplay { Map = "Towers of Doom", GameVersion = "2.57.0.98304" }
        );

        Assert.Equal("Towers of Doom", video.Map);
        Assert.Equal("Master", video.Rank);
        Assert.True(video.IsResolved);
    }

    [Fact]
    public void Read_ClipHasNoModeAndNeedsOnlyItsBuild()
    {
        YouTubeLibraryVideo video = YouTubeVideoFacts.Read(
            "v",
            "Li-Ming - pentakill - Alterac Pass - 65550001",
            "clip:65550001:pentakill:Li-Ming\nHeroes Profile Match: https://www.heroesprofile.com/Match/Single/?replayID=65550001",
            "public"
        );

        Assert.Equal(YouTubeLibraryRecord.Clip, video.Kind);
        Assert.Equal("Alterac Pass", video.Map);
        Assert.Null(video.Mode);
        Assert.False(YouTubeVideoFacts.NeedsRank(video));
        video.GameVersion = "2.57.0.98304";
        Assert.True(video.IsResolved);
    }

    [Fact]
    public void Read_CurrentTemplateKeepsTheDraftNoteAndTheNamedPlayer()
    {
        YouTubeLibraryVideo video = YouTubeVideoFacts.Read(
            "v",
            "Illidan focus - Dragon Shire - Storm League - Diamond 3 - No healer - 65550001",
            "Twitch: https://twitch.tv/saltysadism\r\nFull match.\r\nReplay ID: 65550001\r\nBuild: 2.57.0.98304\r\nMap: Dragon Shire\r\nMode: Storm League\r\nRank: Diamond 3\r\nFeatured: Illidan\r\nDraft: No healer\r\nFeaturing: Xal'atath",
            "public"
        );

        Assert.True(video.IsResolved);
        Assert.Equal("No healer", video.Draft);
        Assert.Equal("Illidan", video.FocusHero);
        Assert.True(video.IsViewerReview);
    }

    [Fact]
    public void Read_ANewHeroIsNotANamedPlayer()
    {
        YouTubeLibraryVideo video = YouTubeVideoFacts.Read(
            "v",
            "Ft. Xal'atath - Volskaya Foundry - Storm League - Diamond - 65389750",
            "Replay ID: 65389750\nBuild: 2.57.0.98304\nFeaturing: Xal'atath\nRequested by: viewer",
            "public"
        );

        Assert.Null(video.FocusHero);
        Assert.Null(video.Draft);
        Assert.False(video.IsViewerReview);
    }

    [Fact]
    public void Learn_FillsOnlyWhatTheRecordLacks()
    {
        var known = new YouTubeLibraryVideo
        {
            VideoId = "v",
            Kind = YouTubeLibraryRecord.Full,
            Draft = "Double tank",
        };
        var shown = new YouTubeLibraryVideo { Draft = "No healer", FocusHero = "Uther" };

        Assert.True(YouTubeVideoFacts.Learn(known, shown));
        Assert.False(YouTubeVideoFacts.Learn(known, shown));
        Assert.Equal("Double tank", known.Draft);
        Assert.Equal("Uther", known.FocusHero);

        var clip = new YouTubeLibraryVideo { VideoId = "c", Kind = YouTubeLibraryRecord.Clip };
        Assert.False(YouTubeVideoFacts.Learn(clip, shown));
        Assert.Null(clip.FocusHero);
    }
}
