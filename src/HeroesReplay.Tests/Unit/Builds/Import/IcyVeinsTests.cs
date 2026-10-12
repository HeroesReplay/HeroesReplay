using HeroesReplay.Core.Builds.Import;
using Xunit;

namespace HeroesReplay.Tests.Unit.Builds.Import;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class IcyVeinsTests
{
    [Theory]
    [InlineData("Li-Ming", "li-ming")]
    [InlineData("Li Li", "li-li")]
    [InlineData("Lúcio", "lucio")]
    [InlineData("Kael'thas", "kaelthas")]
    [InlineData("Xal'atath", "xalatath")]
    [InlineData("Sgt. Hammer", "sgt-hammer")]
    [InlineData("Lt. Morales", "lt-morales")]
    [InlineData("The Lost Vikings", "the-lost-vikings")]
    [InlineData("D.Va", "dva")]
    [InlineData("E.T.C.", "e-t-c")]
    [InlineData("Kel'Thuzad", "kel-thuzad")]
    [InlineData("Cho", "cho")]
    public void Slug_FollowsTheSitesHeroAddresses(string name, string slug)
    {
        Assert.Equal(slug, IcyVeins.Slug(name));
    }

    [Fact]
    public void Urls_AreTheGuideAndTheCalculator()
    {
        Assert.Equal(
            "https://www.icy-veins.com/heroes/valla-build-guide",
            IcyVeins.GuideUrl("valla")
        );
        Assert.Equal(
            "https://www.icy-veins.com/heroes/talent-calculator/valla#23.0!3232234",
            IcyVeins.CalculatorUrl("valla", "23.0!3232234")
        );
        Assert.Null(IcyVeins.CalculatorUrl("deckard", null));
    }
}
