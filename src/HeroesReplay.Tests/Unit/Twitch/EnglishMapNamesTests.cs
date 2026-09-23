using HeroesReplay.Core.Models;
using HeroesReplay.Core.Services.Twitch;
using Xunit;

namespace HeroesReplay.Tests.Unit.Twitch;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class EnglishMapNamesTests
{
    [Fact]
    public void Prefer_UsesTheEnglishCatalogName()
    {
        Assert.Equal(
            "Cursed Hollow",
            EnglishMapNames.Prefer("Cursed Hollow", "Verfluchte Senke", null)
        );
        Assert.Equal("Blackheart's Bay", EnglishMapNames.Canonical("blackheart’s bay"));
        Assert.Equal("Cursed Hollow: who wins?", MatchPrediction.TitleForMap("cursed hollow"));
    }
}
