using HeroesReplay.Core.Services.Observer;
using Xunit;

namespace HeroesReplay.Tests.Unit.Observer;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class MatchEndBannerTests
{
    [Theory]
    [InlineData("MVP")]
    [InlineData("Qhira  MVP")]
    [InlineData("guardian siege master MVP headhunter")]
    public void MvpCountsEvenFarFromCore(string text)
    {
        Assert.True(MatchEndBanner.IsEnd(text, nearCore: false));
    }

    [Theory]
    [InlineData("VICTORY")]
    [InlineData("DEFEAT")]
    [InlineData("Blue team VICTORY")]
    public void VictoryAndDefeatCountOnlyNearCore(string text)
    {
        Assert.False(MatchEndBanner.IsEnd(text, nearCore: false));
        Assert.True(MatchEndBanner.IsEnd(text, nearCore: true));
    }

    [Theory]
    [InlineData("Defeat or bribe this camp to gain Mercenaries")]
    [InlineData("")]
    [InlineData("WELCOME TO ALTERAC PASS")]
    [InlineData("18:53")]
    public void CampTooltipAndOrdinaryHudAreNotTheEnd(string text)
    {
        Assert.False(MatchEndBanner.IsEnd(text, nearCore: false));
        Assert.False(MatchEndBanner.IsEnd(text, nearCore: true));
    }
}
