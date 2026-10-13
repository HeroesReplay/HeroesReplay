using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class HomeScreenCueTests
{
    [Fact]
    public void Sees_HomeInMemory_IsHome()
    {
        // 2026-10-02: the next client was not in front and captured black. Memory said home.
        // 2026-10-08 17:39:14 (#292 release proof): OCR still read the AUTHENTICATION panel while
        // memory read ScreenHome; the saved frame showed home.
        Assert.True(HomeScreenCue.Sees(homeInMemory: true, menuInMemory: true));
        Assert.True(HomeScreenCue.Sees(homeInMemory: true, menuInMemory: null));
    }

    [Fact]
    public void Sees_AMenuThatIsNotTheHomeScreen_IsNotHome()
    {
        // #292: LoadingScreen read Battle.net's AUTHENTICATION panel, the email/password form and
        // the DOWNLOADING dialog as a menu, and the replay was opened on them (2026-10-08, 13:40
        // and 14:12). The client's own screens say none is home.
        Assert.False(HomeScreenCue.Sees(homeInMemory: false, menuInMemory: true));
    }

    [Fact]
    public void Sees_TheMenuScreensCannotTell_AMenuInMemoryIsHome()
    {
        Assert.True(HomeScreenCue.Sees(homeInMemory: null, menuInMemory: true));
    }

    [Fact]
    public void Sees_LoadingOrMatchOrNothingInMemory_IsNotHome()
    {
        Assert.False(HomeScreenCue.Sees(homeInMemory: false, menuInMemory: false));
        Assert.False(HomeScreenCue.Sees(homeInMemory: null, menuInMemory: false));
        Assert.False(HomeScreenCue.Sees(homeInMemory: null, menuInMemory: null));
    }
}
