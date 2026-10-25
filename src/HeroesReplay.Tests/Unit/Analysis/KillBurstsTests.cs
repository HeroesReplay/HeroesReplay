using HeroesReplay.Core.Analysis;
using Xunit;

namespace HeroesReplay.Tests.Unit.Analysis;

/// <summary>One hero's kills in a quick burst (#511): bounded by the span from the first kill.</summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class KillBurstsTests
{
    [Fact]
    public void ThreeKillsWithinThreeSeconds_AreOneBurst()
    {
        KillBurst burst = Assert.Single(KillBursts.Find(new[] { 10, 50, 51, 53, 90 }));

        Assert.Equal(new KillBurst(1, 3, 50, 53), burst);
    }

    [Fact]
    public void AFourthKillInsideTheSpan_JoinsTheBurst()
    {
        Assert.Equal(
            new[] { new KillBurst(0, 4, 50, 53) },
            KillBursts.Find(new[] { 50, 51, 52, 53, 57 })
        );
    }

    [Fact]
    public void AGapRuleChainIsNotABurst()
    {
        // Three kills two seconds apart chain under KillStreaks, but span four seconds.
        Assert.Empty(KillBursts.Find(new[] { 50, 52, 54 }));
    }

    [Fact]
    public void BurstsNeverOverlap()
    {
        Assert.Equal(
            new[] { new KillBurst(0, 3, 50, 52), new KillBurst(3, 3, 54, 56) },
            KillBursts.Find(new[] { 50, 51, 52, 54, 55, 56 })
        );
    }

    [Fact]
    public void TooFewKills_AreNoBurst()
    {
        Assert.Empty(KillBursts.Find(new[] { 50, 51 }));
        Assert.Empty(KillBursts.Find(null));
    }
}
