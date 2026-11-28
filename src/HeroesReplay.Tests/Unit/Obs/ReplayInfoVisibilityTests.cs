using System;
using HeroesReplay.Core.Obs;
using Xunit;

namespace HeroesReplay.Tests.Unit.Obs;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayInfoVisibilityTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(120, false)]
    public void ShouldShow_KeepsTheCaptionForTheOpeningThirtySeconds(int seconds, bool show)
    {
        Assert.Equal(
            show,
            ReplayInfoVisibility.ShouldShow(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(30))
        );
    }

    [Fact]
    public void ShouldShow_FallsBackToTheDefaultWhenUnset()
    {
        Assert.True(ReplayInfoVisibility.ShouldShow(TimeSpan.FromSeconds(29), TimeSpan.Zero));
        Assert.False(ReplayInfoVisibility.ShouldShow(TimeSpan.FromSeconds(30), TimeSpan.Zero));
    }
}
