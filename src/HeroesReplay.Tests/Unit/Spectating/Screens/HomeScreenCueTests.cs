using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HomeScreenCueTests
{
    [Fact]
    public void Sees_HomeInMemoryWithABlackCapture_IsHome()
    {
        // 2026-10-02: the next client was not in front and captured black. Memory said home.
        Assert.True(
            HomeScreenCue.Sees(
                homeInMemory: true,
                menuInMemory: true,
                ocrFoundHome: false,
                ocrText: ""
            )
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("AUTHENTICATION Connecting... Cancel")]
    [InlineData(
        "DOWNLOADING All data files must be fully downloaded to load this version of the game."
    )]
    public void Sees_AMenuThatIsNotTheHomeScreen_IsNotHome(string ocrText)
    {
        // #292: LoadingScreenMemory read Battle.net's AUTHENTICATION panel and the DOWNLOADING
        // screen as a menu, and the replay was opened on them (2026-10-08, 13:40 and 14:12). The
        // client's own screens say neither is home.
        Assert.False(
            HomeScreenCue.Sees(
                homeInMemory: false,
                menuInMemory: true,
                ocrFoundHome: false,
                ocrText: ocrText
            )
        );
    }

    [Fact]
    public void Sees_AMenuWithALoginForm_IsNotHome()
    {
        Assert.False(
            HomeScreenCue.Sees(
                homeInMemory: null,
                menuInMemory: true,
                ocrFoundHome: false,
                ocrText: "Email Password Log In"
            )
        );
        Assert.False(
            HomeScreenCue.Sees(
                homeInMemory: true,
                menuInMemory: true,
                ocrFoundHome: false,
                ocrText: "Email Password Log In"
            )
        );
    }

    [Fact]
    public void Sees_TheMenuScreensCannotTell_AMenuInMemoryIsHome()
    {
        Assert.True(
            HomeScreenCue.Sees(
                homeInMemory: null,
                menuInMemory: true,
                ocrFoundHome: false,
                ocrText: ""
            )
        );
    }

    [Fact]
    public void Sees_LoadingOrMatchInMemory_IsNotHomeWhateverOcrReads()
    {
        Assert.False(
            HomeScreenCue.Sees(
                homeInMemory: false,
                menuInMemory: false,
                ocrFoundHome: true,
                ocrText: "PLAY"
            )
        );
        Assert.False(
            HomeScreenCue.Sees(
                homeInMemory: null,
                menuInMemory: false,
                ocrFoundHome: true,
                ocrText: "PLAY"
            )
        );
    }

    [Fact]
    public void Sees_MemoryCannotTell_UsesOcr()
    {
        Assert.True(
            HomeScreenCue.Sees(
                homeInMemory: null,
                menuInMemory: null,
                ocrFoundHome: true,
                ocrText: "PLAY"
            )
        );
        Assert.False(
            HomeScreenCue.Sees(
                homeInMemory: null,
                menuInMemory: null,
                ocrFoundHome: false,
                ocrText: ""
            )
        );
    }
}
