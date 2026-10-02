using HeroesReplay.Core.Spectating.Session;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Session;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HomeScreenCueTests
{
    [Fact]
    public void Sees_MenuInMemoryWithABlackCapture_IsHome()
    {
        // 2026-10-02: the next client was not in front and captured black. Memory said menu.
        Assert.True(HomeScreenCue.Sees(menuInMemory: true, ocrFoundHome: false, ocrText: ""));
    }

    [Fact]
    public void Sees_MenuInMemoryWithALoginForm_IsNotHome()
    {
        Assert.False(
            HomeScreenCue.Sees(
                menuInMemory: true,
                ocrFoundHome: false,
                ocrText: "Email Password Log In"
            )
        );
    }

    [Fact]
    public void Sees_LoadingOrMatchInMemory_IsNotHomeWhateverOcrReads()
    {
        Assert.False(HomeScreenCue.Sees(menuInMemory: false, ocrFoundHome: true, ocrText: "PLAY"));
    }

    [Fact]
    public void Sees_MemoryCannotTell_UsesOcr()
    {
        Assert.True(HomeScreenCue.Sees(menuInMemory: null, ocrFoundHome: true, ocrText: "PLAY"));
        Assert.False(HomeScreenCue.Sees(menuInMemory: null, ocrFoundHome: false, ocrText: ""));
    }
}
