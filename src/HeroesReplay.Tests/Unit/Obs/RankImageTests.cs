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

    [Fact]
    public void SourceName_FromLeagueTier()
    {
        Assert.Equal("diamond-image", RankImage.SourceName(null, 5));
    }

    [Theory]
    [InlineData(1700, "Bronze")]
    [InlineData(2977, "Diamond")]
    [InlineData(3400, "Grandmaster")]
    public void FromAverageMmr_MapsLobby(double mmr, string rank)
    {
        Assert.Equal(rank, RankImage.FromAverageMmr(mmr));
    }
}
