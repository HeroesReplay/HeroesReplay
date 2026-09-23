using System;
using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class EndScreenHoldTests
{
    [Fact]
    public void Duration_EndsSoonerOnceMvpIsSeen()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(20),
            EndScreenHold.Duration(true, TimeSpan.FromSeconds(75))
        );
        Assert.Equal(
            TimeSpan.FromSeconds(35),
            EndScreenHold.Duration(false, TimeSpan.FromSeconds(75))
        );
    }
}
