using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.YouTube;
using Xunit;

namespace HeroesReplay.Tests.Unit.YouTube;

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
    public void For_ReadsStoredFieldsBeforeTheTitle()
    {
        string playlist = YouTubePlaylistNames.For(
            new YouTubeEntry
            {
                Map = "Cursed Hollow",
                GameType = "Storm League",
                Rank = "Diamond 2",
                Title = "ignored",
            }
        );

        Assert.Equal("Cursed Hollow - Storm League - Diamond", playlist);
    }

    [Fact]
    public void FromTitle_ParsesAnOlderEntry()
    {
        Assert.Equal(
            "Volskaya Foundry - Storm League - Diamond",
            YouTubePlaylistNames.FromTitle("Volskaya Foundry - 65389750 - Storm League - Diamond")
        );
        Assert.Null(YouTubePlaylistNames.FromTitle("Dragon Shire - 65396086 - Platinum"));
    }
}
