using HeroesReplay.Core.Spectating.Control;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Control;

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

    [Fact]
    public void AwardBoardEndsTheLaunchWait()
    {
        const string text =
            "GUARDIAN 40% of Team Fight Damage Soaked SIEGE MASTER 34% of Team's Structure Damage CLUTCH";

        Assert.True(MatchEndBanner.IsEnd(text, nearCore: false));
        Assert.True(MatchEndBanner.EndsLaunchWait(text));
        Assert.True(MatchEndBanner.EndsLaunchWait("SIEGE MASTER"));
        Assert.False(MatchEndBanner.EndsLaunchWait("18:53"));
        Assert.False(MatchEndBanner.EndsLaunchWait("WELCOME TO ALTERAC PASS"));
        Assert.False(
            MatchEndBanner.EndsLaunchWait("Defeat or bribe this camp to gain Mercenaries")
        );
        Assert.False(MatchEndBanner.EndsLaunchWait("VICTORY"));
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
