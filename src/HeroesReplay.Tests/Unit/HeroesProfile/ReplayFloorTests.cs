using HeroesReplay.Core.HeroesProfile;
using Xunit;

namespace HeroesReplay.Tests.Unit.HeroesProfile;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayFloorTests
{
    [Theory]
    [InlineData("2.57.0.98304", true)]
    [InlineData("2.57.0.98285", true)]
    [InlineData("2.55.17.98025", false)]
    [InlineData("", false)]
    public void Allows_UsesTheConfiguredClientFloor(string version, bool allowed)
    {
        Assert.Equal(allowed, ReplayFloor.Allows(version, exactVersions: null, "2.57.0.98285"));
    }
}
