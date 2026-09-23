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
        Assert.Equal("Dragon Shire", EnglishMapNames.Canonical("DragonShire"));
        Assert.Equal("Garden of Terror", EnglishMapNames.Canonical("HauntedWoods"));
        Assert.Equal("Infernal Shrines", EnglishMapNames.Canonical("Shrines"));
        Assert.Equal(
            "Dragon Shire",
            EnglishMapNames.Prefer("용의 둥지", "용의 둥지", "DragonShire")
        );
        Assert.Equal(
            "Braxis Holdout",
            EnglishMapNames.Prefer("Le laboratoire de Braxis", null, "BraxisHoldout")
        );
        Assert.Equal("Dragon Shire", EnglishMapNames.Canonical("용의 둥지"));
        Assert.Equal("Cursed Hollow: who wins?", MatchPrediction.TitleForMap("cursed hollow"));
    }
}
