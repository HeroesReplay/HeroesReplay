using System.Collections.Generic;
using HeroesReplay.Core.YouTube;
using HeroesReplay.Core.YouTube.Playlists;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Playlists;

/// <summary>
/// The library reads a focus-hero video back from its <c>Hero focus:</c> line (#433) and files it
/// into "Hero gameplay", never into the viewer reviews.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class HeroFocusPlaylistTests
{
    private const string Line = "2.57.0.98348";

    [Theory]
    [InlineData(
        "Hero focus: Li-Ming (Teleport Build), Ranged Assassin",
        "Li-Ming",
        "Ranged Assassin"
    )]
    [InlineData("Hero focus: Rexxar, Bruiser", "Rexxar", "Bruiser")]
    [InlineData("Hero focus: Lt. Morales (Medivac Build)", "Lt. Morales", null)]
    [InlineData("Hero focus: Cho, Not a role", "Cho, Not a role", null)]
    public void TheHeroFocusLine_GivesTheHeroAndARole(string line, string hero, string role)
    {
        (string Hero, string Role) read = YouTubeVideoFacts.HeroFocus(new[] { "Map: x", line });

        Assert.Equal(hero, read.Hero);
        Assert.Equal(role, read.Role);
    }

    [Fact]
    public void TheChannelAndTheEntry_ReadTheSameFocus()
    {
        string[] lines =
        {
            "Replay ID: 65550003",
            "Map: Alterac Pass",
            "Mode: Storm League",
            "Rank: Diamond",
            "Hero focus: Li-Ming (Teleport Build), Ranged Assassin",
            "Build: " + Line,
        };
        YouTubeLibraryVideo shown = YouTubeVideoFacts.Read(
            "v1",
            "Teleport Li-Ming gameplay - 12/0/9 - Alterac Pass - Storm League - Diamond - 65550003",
            string.Join("\n", lines),
            "public"
        );
        YouTubeLibraryVideo recorded = YouTubeLibraryRecord.FromEntry(
            new YouTubeEntry
            {
                VideoId = "v1",
                ReplayId = 65550003,
                Map = "Alterac Pass",
                GameType = "Storm League",
                GameVersion = Line,
                DescriptionLines = lines,
            },
            null
        );

        foreach (YouTubeLibraryVideo video in new[] { shown, recorded })
        {
            Assert.Equal("Li-Ming", video.HeroFocus);
            Assert.Equal("Ranged Assassin", video.HeroFocusRole);
            Assert.Null(video.FocusHero);
            Assert.False(video.IsViewerReview);
        }
    }

    [Fact]
    public void Learn_CopiesAFocusTheRecordDoesNotHaveYet()
    {
        var known = new YouTubeLibraryVideo { VideoId = "v1", Kind = YouTubeLibraryRecord.Full };
        var shown = new YouTubeLibraryVideo
        {
            VideoId = "v1",
            Kind = YouTubeLibraryRecord.Full,
            HeroFocus = "Li-Ming",
            HeroFocusRole = "Ranged Assassin",
        };

        Assert.True(YouTubeVideoFacts.Learn(known, shown));
        Assert.Equal("Li-Ming", known.HeroFocus);
        Assert.Equal("Ranged Assassin", known.HeroFocusRole);
        Assert.False(YouTubeVideoFacts.Learn(known, shown));
    }

    [Fact]
    public void AFocusVideo_IsFiledUnderHeroGameplayOnlyByDefault()
    {
        IReadOnlyList<string> titles = YouTubeLibraryPlanner.Titles(
            Video("Li-Ming", "Ranged Assassin"),
            new YouTubePlaylistSettings()
        );

        Assert.Contains(YouTubePlaylistNames.HeroGameplay, titles);
        Assert.DoesNotContain(titles, title => title.StartsWith("Hero gameplay - "));
        Assert.DoesNotContain(YouTubePlaylistNames.ViewerReviews, titles);
    }

    [Fact]
    public void TheFocusGroups_FollowTheirSwitches_AndAnUnknownRoleHasNoRolePlaylist()
    {
        IReadOnlyList<string> off = YouTubeLibraryPlanner.Titles(
            Video("Li-Ming", "Ranged Assassin"),
            new YouTubePlaylistSettings { HeroFocus = false, HeroFocusRole = false }
        );
        IReadOnlyList<string> role = YouTubeLibraryPlanner.Titles(
            Video("Li-Ming", "Ranged Assassin"),
            new YouTubePlaylistSettings { HeroFocusRole = true }
        );
        IReadOnlyList<string> noRole = YouTubeLibraryPlanner.Titles(
            Video("Li-Ming", null),
            new YouTubePlaylistSettings { HeroFocusRole = true }
        );
        IReadOnlyList<string> ordinary = YouTubeLibraryPlanner.Titles(
            Video(null, null),
            new YouTubePlaylistSettings()
        );

        Assert.DoesNotContain(off, title => title.StartsWith(YouTubePlaylistNames.HeroGameplay));
        Assert.Contains("Hero gameplay - Ranged Assassin", role);
        Assert.Contains(YouTubePlaylistNames.HeroGameplay, noRole);
        Assert.DoesNotContain(noRole, title => title.StartsWith("Hero gameplay - "));
        Assert.DoesNotContain(
            ordinary,
            title => title.StartsWith(YouTubePlaylistNames.HeroGameplay)
        );
        Assert.Null(YouTubePlaylistNames.HeroFocusRole("Wizard"));
        Assert.Equal("Hero gameplay - Healer", YouTubePlaylistNames.HeroFocusRole("healer"));
    }

    private static YouTubeLibraryVideo Video(string hero, string role) =>
        new()
        {
            VideoId = "v1",
            ReplayId = 65550003,
            Kind = YouTubeLibraryRecord.Full,
            Map = "Alterac Pass",
            Mode = "Storm League",
            Rank = "Diamond",
            GameVersion = Line,
            PrivacyStatus = "public",
            HeroFocus = hero,
            HeroFocusRole = role,
        };
}
