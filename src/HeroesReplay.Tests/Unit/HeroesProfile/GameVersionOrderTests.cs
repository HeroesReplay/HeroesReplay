using HeroesReplay.Core.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class GameVersionOrderTests
{
    [Theory]
    [InlineData("2.57.0.98285", true)]
    [InlineData("2.57.0.98297", true)]
    [InlineData("2.57.0.99000", true)]
    [InlineData("2.57.1.1", true)]
    [InlineData("2.58.0.1", true)]
    [InlineData("2.55.17.98025", false)]
    [InlineData("2.56.99.99999", false)]
    [InlineData("2.57.0.98284", false)]
    public void IsAtLeast_AcceptsTheSeptemberPatchAndNewerBuilds(string version, bool allowed)
    {
        Assert.Equal(allowed, GameVersionOrder.IsAtLeast(version, "2.57.0.98285"));
    }

    [Theory]
    [InlineData("2.57.0.98285", "2.57")]
    [InlineData("2.57.0.98304", "2.57")]
    [InlineData("2, 57, 0, 98297", "2.57")]
    [InlineData("2.57.1.10", "2.57")]
    [InlineData("2.55.17.98025", "2.55")]
    [InlineData("2.58.0.1", "2.58")]
    public void PatchLine_UsesTheFirstTwoNumbers(string version, string line)
    {
        Assert.Equal(line, GameVersionOrder.PatchLine(version));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("")]
    [InlineData("beta.57")]
    [InlineData(null)]
    public void PatchLine_RequiresTwoNumericParts(string version)
    {
        Assert.Null(GameVersionOrder.PatchLine(version));
    }

    [Theory]
    [InlineData("2.57.0.98285", "2.57.0.98304", true)]
    [InlineData("2.57.0.98304", "2.57.1.1", true)]
    [InlineData("2.57.0.98304", "2.55.17.98025", false)]
    [InlineData("2.57.0.98304", "2.58.0.1", false)]
    public void SamePatch_GroupsBuildIterations(string left, string right, bool same)
    {
        Assert.Equal(same, GameVersionOrder.SamePatch(left, right));
    }
}
