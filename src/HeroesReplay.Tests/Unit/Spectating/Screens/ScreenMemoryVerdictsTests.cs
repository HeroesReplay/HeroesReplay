using HeroesClientSDK;
using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ScreenMemoryVerdictsTests
{
    private static readonly LoadingScreenSample Menu = new(
        ClientScreen.Menu,
        MenuSeen: true,
        "menu"
    );

    private static readonly LoadingScreenSample LoadingAfterMenu = new(
        ClientScreen.Loading,
        MenuSeen: true,
        "loading"
    );

    private static readonly LoadingScreenSample LoadingBeforeMenu = new(
        ClientScreen.Loading,
        MenuSeen: false,
        "loading"
    );

    private static readonly LoadingScreenSample Match = new(
        ClientScreen.Match,
        MenuSeen: true,
        "match"
    );

    private static readonly LoadingScreenSample Unknown = new(
        ClientScreen.Unknown,
        MenuSeen: true,
        "no-state"
    );

    [Fact]
    public void For_Menu_IsHomeAndNotMapLoading()
    {
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.Home, Menu));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.MapLoading, Menu));
    }

    [Fact]
    public void For_LoadingAfterMenu_IsMapLoadingAndNotHome()
    {
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, LoadingAfterMenu));
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.MapLoading, LoadingAfterMenu));
    }

    [Fact]
    public void For_LoadingBeforeMenu_CannotTell()
    {
        // The boot splash is a loading screen too, so memory cannot tell before the first menu.
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.Home, LoadingBeforeMenu));
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.MapLoading, LoadingBeforeMenu));
    }

    [Fact]
    public void For_Match_IsNeitherHomeNorMapLoading()
    {
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, Match));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.MapLoading, Match));
    }

    [Fact]
    public void For_UnknownOrNoSample_CannotTell()
    {
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.Home, Unknown));
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.MapLoading, Unknown));
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.Home, null));
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.MapLoading, null));
    }

    [Theory]
    [InlineData(ScreenState.LoginForm)]
    [InlineData(ScreenState.GameDataDownload)]
    [InlineData(ScreenState.GameDataStartup)]
    [InlineData(ScreenState.VersionMismatch)]
    [InlineData(ScreenState.RegionUnavailable)]
    [InlineData(ScreenState.EndScreen)]
    public void For_StatesMemoryDoesNotReadYet_CannotTell(ScreenState state)
    {
        foreach (
            LoadingScreenSample? sample in new LoadingScreenSample?[]
            {
                Menu,
                LoadingAfterMenu,
                LoadingBeforeMenu,
                Match,
                Unknown,
                null,
            }
        )
        {
            Assert.Null(ScreenMemoryVerdicts.For(state, sample));
        }
    }

    [Fact]
    public void Describe_NamesTheScreenReasonAndMenuSeen()
    {
        Assert.Equal(
            "Loading, loading, menu seen True",
            ScreenMemoryVerdicts.Describe(LoadingAfterMenu)
        );
        Assert.Equal("not read", ScreenMemoryVerdicts.Describe(null));
    }
}
