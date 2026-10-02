using HeroesReplay.Core.YouTube.Playlists;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube.Playlists;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class YouTubePlaylistNameTests
{
    [Fact]
    public void Title_DropsTheStormLeagueDivision()
    {
        Assert.Equal(
            "Cursed Hollow - Storm League - Gold",
            YouTubePlaylistNames.Title("Cursed Hollow", "Storm League", "Gold 3")
        );
        Assert.Equal("Grandmaster", YouTubePlaylistNames.League("Grand Master 1"));
        Assert.Equal(
            "Towers of Doom - Storm League - Grandmaster",
            YouTubePlaylistNames.Title("Towers of Doom", "storm league", "grandmaster")
        );
    }

    [Fact]
    public void Title_UsesTheModeWithoutALeague()
    {
        Assert.Equal(
            "Sky Temple - Quick Match",
            YouTubePlaylistNames.Title("Sky Temple", "Quick Match", "Gold 2")
        );
        Assert.Equal(
            "Braxis Holdout - ARAM",
            YouTubePlaylistNames.Title("Braxis Holdout", "ARAM", null)
        );
        Assert.Equal(
            "Dragon Shire - Unranked Draft",
            YouTubePlaylistNames.Title("Dragon Shire", "Unranked Draft", null)
        );
        Assert.Equal(
            "Volskaya Foundry - Storm League",
            YouTubePlaylistNames.Title("Volskaya Foundry", "Storm League", null)
        );
        Assert.Null(YouTubePlaylistNames.Title("Alterac Pass", "Custom", null));
    }

    [Fact]
    public void Map_IsTheEnglishCatalogName()
    {
        Assert.Equal("Dragon Shire", YouTubePlaylistNames.Map("용의 둥지"));
        Assert.Equal("Blackheart's Bay", YouTubePlaylistNames.Map("Blackheart’s Bay"));
        Assert.Equal("Silver City", YouTubePlaylistNames.Map("silver city"));
        Assert.Null(YouTubePlaylistNames.Map("Wieże Zagłady"));
        Assert.Null(YouTubePlaylistNames.Map(null));
    }

    [Fact]
    public void Rank_IsTheStormLeagueTierWithoutDivision()
    {
        Assert.Equal(
            "Storm League - Diamond",
            YouTubePlaylistNames.Rank("Storm League", "Diamond 2")
        );
        Assert.Equal(
            "Storm League - Grandmaster",
            YouTubePlaylistNames.Rank("storm league", "Grand Master")
        );
        Assert.Null(YouTubePlaylistNames.Rank("Storm League", null));
        Assert.Null(YouTubePlaylistNames.Rank("Storm League", "Unranked"));
        Assert.Null(YouTubePlaylistNames.Rank("Quick Match", "Gold 2"));
    }

    [Fact]
    public void DraftNotes_DropTheTeamAndKeepEachNoteOnce()
    {
        Assert.Equal(
            new[] { "No tank", "Double healer" },
            YouTubePlaylistNames.DraftNotes("Blue no tank, Red double healer")
        );
        Assert.Equal(
            new[] { "No tank" },
            YouTubePlaylistNames.DraftNotes("Blue no tank, Red no tank")
        );
        Assert.Equal(new[] { "No healer" }, YouTubePlaylistNames.DraftNotes("No healer"));
        Assert.Empty(YouTubePlaylistNames.DraftNotes(null));
        Assert.Equal(
            new[] { "Unusual drafts - No tank or healer" },
            YouTubePlaylistNames.Drafts("Red no tank or healer")
        );
    }

    [Fact]
    public void Fit_KeepsATitleYouTubeAccepts()
    {
        Assert.Equal(150, YouTubePlaylistNames.Fit(new string('a', 151)).Length);
        Assert.Equal("Season one", YouTubePlaylistNames.Fit(" <Season one> "));
        Assert.Null(YouTubePlaylistNames.Fit("<>"));
    }
}
