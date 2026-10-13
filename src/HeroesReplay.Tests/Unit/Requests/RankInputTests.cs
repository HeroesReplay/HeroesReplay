using HeroesReplay.Core.Requests;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Requests;

/// <summary>
/// #429: what a viewer types for a rank reward. Before, only an exact enum name ("Gold") was
/// read, and anything else ("Plat", "gold 3") queued a game at any rank.
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class RankInputTests
{
    [Theory]
    [InlineData("Gold", GameRank.Gold)]
    [InlineData("gold 3", GameRank.Gold)]
    [InlineData("  DIAMOND  ", GameRank.Diamond)]
    [InlineData("Plat", GameRank.Platinum)]
    [InlineData("platinum 2", GameRank.Platinum)]
    [InlineData("Bronze", GameRank.Bronze)]
    [InlineData("silver", GameRank.Silver)]
    [InlineData("master", GameRank.Master)]
    [InlineData("GM", GameRank.Master)]
    [InlineData("Grandmaster", GameRank.Master)]
    public void ALeagueName_IsRead(string text, GameRank expected)
    {
        Assert.Equal(expected, RankInput.Read(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("3")]
    [InlineData("the best")]
    public void TextThatNamesNoLeague_IsNoRank(string text)
    {
        Assert.Null(RankInput.Read(text));
    }

    [Fact]
    public void Names_ListsEveryRank()
    {
        Assert.Equal("Bronze, Silver, Gold, Platinum, Diamond or Master", RankInput.Names);
    }
}
