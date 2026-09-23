using HeroesReplay.Core.Services.HeroesProfile;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class RankImageTests
{
    [Theory]
    [InlineData("Platinum", "platinum-image")]
    [InlineData("platinum 2", "platinum-image")]
    [InlineData("GM", "grandmaster-image")]
    [InlineData("woood", "bronze-image")]
    [InlineData(null, null)]
    public void SourceName_FromRankString(string rank, string expected)
    {
        Assert.Equal(expected, RankImage.SourceName(rank));
    }

    [Theory]
    [InlineData("65268389_Storm League_Diamond_Sky Temple_4f570172_.StormReplay", "Diamond")]
    [InlineData("1_Storm League_Unknown_Tomb of the Spider Queen_.StormReplay", null)]
    [InlineData("not-a-cache-name.StormReplay", null)]
    public void RankFromCacheFileName_ReadsTheThirdSegment(string fileName, string expected)
    {
        Assert.Equal(expected, RankImage.RankFromCacheFileName(fileName));
    }

    [Theory]
    [InlineData("Diamond 3", "3")]
    [InlineData("platinum-1", "1")]
    [InlineData("Gold 3", "3")]
    [InlineData("Bronze 5", "5")]
    [InlineData("Master", null)]
    [InlineData("Grandmaster", null)]
    [InlineData("Grandmaster 4200", null)]
    [InlineData("Gold", null)]
    [InlineData("Platinum", null)]
    public void Division_ReadsOneThroughFive(string rank, string expected)
    {
        Assert.Equal(expected, RankImage.Division(rank));
    }

    [Theory]
    [InlineData("Gold 3", "gold-image", "3")]
    [InlineData("Bronze 5", "bronze-image", "5")]
    [InlineData("Master", "master-image", null)]
    [InlineData("Grandmaster", "grandmaster-image", null)]
    [InlineData("Gold", "gold-image", null)]
    [InlineData("Platinum", "platinum-image", null)]
    public void Badge_UsesLeagueImageAndDivisionOnlyWhenPresent(
        string rank,
        string image,
        string division
    )
    {
        Assert.Equal(image, RankImage.SourceName(rank));
        Assert.Equal(division, RankImage.Division(rank));
    }

    [Fact]
    public void PointsText_RoundsAverageMmr()
    {
        Assert.Equal("2670", RankImage.PointsText(2669.6));
        Assert.Null(RankImage.PointsText(null));
    }

    [Fact]
    public void SourceName_FromLeagueTier()
    {
        Assert.Equal("diamond-image", RankImage.SourceName(null, 5));
    }

    [Theory]
    [InlineData(1700, "Bronze")]
    [InlineData(2977, "Diamond")]
    [InlineData(3400, "Grandmaster")]
    public void FromAverageMmr_MapsLobby(double mmr, string staleLadder)
    {
        Assert.True(mmr > 0);
        Assert.Null(HeroesProfileRankEnricher.Resolve("Storm League", staleLadder, null));
    }
}
