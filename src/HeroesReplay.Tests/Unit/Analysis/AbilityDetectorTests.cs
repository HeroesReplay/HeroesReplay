using HeroesReplay.Core.Services.Analysis;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class AbilityDetectorTests
{
    [Fact]
    public void MatchesAnyBuildEntry()
    {
        Assert.True(
            AbilityDetector.IsBuildInRange(98025, greaterEqualBuild: 68740, lessThanBuild: null)
        );
        Assert.True(
            AbilityDetector.IsBuildInRange(68739, greaterEqualBuild: null, lessThanBuild: 68740)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(68740, greaterEqualBuild: null, lessThanBuild: 68740)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(68739, greaterEqualBuild: 68740, lessThanBuild: null)
        );
        Assert.False(
            AbilityDetector.IsBuildInRange(70000, greaterEqualBuild: 70682, lessThanBuild: 68740)
        );
    }
}
