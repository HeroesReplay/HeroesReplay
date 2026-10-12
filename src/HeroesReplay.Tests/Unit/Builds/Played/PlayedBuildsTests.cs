using System.Linq;
using HeroesReplay.Core.Builds.Played;
using HeroesReplay.Core.Shared;
using Xunit;

namespace HeroesReplay.Tests.Unit.Builds.Played;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class PlayedBuildsTests
{
    [Fact]
    public void Read_MapsTalentIdsToCatalogTalentsInTierOrder()
    {
        Hero hero = BuildFixtures.Hero();

        PlayedBuild build = PlayedBuilds.Read(hero, new[] { "T31", "T02", "T13" });

        Assert.Equal(new[] { "T02", "T13", "T31" }, build.Picks.Select(pick => pick.TalentId));
        Assert.Equal("Talent 1-3", build.At(1).Name);
        Assert.Null(build.At(2));
    }

    [Fact]
    public void Read_FallsBackToTheButtonId()
    {
        PlayedBuild build = PlayedBuilds.Read(BuildFixtures.Hero(), new[] { "B42" });

        Assert.Equal("T42", Assert.Single(build.Picks).TalentId);
    }

    [Fact]
    public void Read_SkipsUnknownBlankAndASecondPickInOneTier()
    {
        PlayedBuild build = PlayedBuilds.Read(
            BuildFixtures.Hero(),
            new[] { "T01", null, " ", "Nope", "T02", "T11" }
        );

        Assert.Equal(new[] { "T01", "T11" }, build.Picks.Select(pick => pick.TalentId));
    }

    [Fact]
    public void Read_WithoutAHero_IsNull()
    {
        Assert.Null(PlayedBuilds.Read(null, new[] { "T01" }));
    }
}
