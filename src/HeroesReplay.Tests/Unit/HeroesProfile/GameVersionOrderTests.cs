using HeroesReplay.Core.Services.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class GameVersionOrderTests
{
    [Theory]
    [InlineData("2.57.0.98285", true)]
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
}
