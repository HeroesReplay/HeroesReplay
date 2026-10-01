using HeroesReplay.Core.Services.Client;
using Xunit;

namespace HeroesReplay.Tests.Unit.Client;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class LauncherButtonTextTests
{
    [Theory]
    [InlineData("Heroes of the Storm Play", "Play")]
    [InlineData("Heroes of the Storm Playing Now", "Playing")]
    [InlineData("HOME GAMES Playing Now", "Playing")]
    [InlineData("Update", "Update")]
    [InlineData("Updating 42%", "Updating")]
    [InlineData("Update and Play", "Update")]
    [InlineData("", "unreadable")]
    [InlineData("News and shop", "unknown")]
    [InlineData("HOME GAMES SHOP Heroes of the Storm", "hidden")]
    [InlineData("HOME GAMES Play", "Play")]
    public void Classify_NamesTheLauncherButton(string text, string expected)
    {
        Assert.Equal(expected, LauncherButtonText.Classify(text));
    }
}
