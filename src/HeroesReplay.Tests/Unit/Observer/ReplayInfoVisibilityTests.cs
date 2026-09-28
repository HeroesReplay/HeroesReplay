using System;
using HeroesReplay.Core.Services.OpenBroadcasterSoftware;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayInfoVisibilityTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(44, true)]
    [InlineData(45, false)]
    [InlineData(120, false)]
    public void ShouldShow_KeepsTheCaptionForTheOpeningFortyFiveSeconds(int seconds, bool show)
    {
        Assert.Equal(
            show,
            ReplayInfoVisibility.ShouldShow(TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(45))
        );
    }
}
