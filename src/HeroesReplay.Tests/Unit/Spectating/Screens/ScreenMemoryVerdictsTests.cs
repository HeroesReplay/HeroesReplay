using HeroesClientSDK;
using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

/// <summary>
/// The shadow verdicts from the HeroesClientSDK menu screens. The samples are what the SDK read
/// from live clients on ASA-SERVER on 2026-10-08 (2.57.0.98348 and 2.57.0.98304).
/// </summary>
[Trait(TestCategories.Category, TestCategories.Unit)]
public class ScreenMemoryVerdictsTests
{
    private static readonly HeroesClientVersion Current = new(2, 57, 0, 98348);

    // Battle.net "launch Hero": the signed-in home screen (mask 0x6181).
    private static readonly ClientScreenSample Home = Sample(
        ClientScreenKind.Home,
        menuSeen: true,
        "ScreenBackgroundHero",
        "ScreenHeroCutscene",
        "ScreenHome",
        "ScreenNavigationHero",
        "ScreenForegroundHero"
    );

    // HeroesSwitcher without SSO: the email/password form (mask 0x60C1).
    private static readonly ClientScreenSample Login = Sample(
        ClientScreenKind.Login,
        menuSeen: true,
        "ScreenBackgroundHero",
        "ScreenLoginUnified",
        "ScreenHeroCutscene",
        "ScreenNavigationHero",
        "ScreenForegroundHero"
    );

    // The boot splash (mask 0x20), before any menu.
    private static readonly ClientScreenSample Boot = Sample(
        ClientScreenKind.Loading,
        menuSeen: false,
        "ScreenLoading"
    );

    // A replay opened from home: the map loading screen.
    private static readonly ClientScreenSample MapLoading = Sample(
        ClientScreenKind.Loading,
        menuSeen: true,
        "ScreenLoading"
    );

    private static readonly ClientScreenSample Match = new(
        ClientScreenKind.Match,
        new string[0],
        "match",
        Current,
        false,
        MenuSeen: true
    );

    private static readonly ClientScreenSample Unknown = new(
        ClientScreenKind.Unknown,
        new string[0],
        "unsupported-build",
        Current,
        false
    );

    [Fact]
    public void For_Home_IsHomeAndNotTheLoginFormOrMapLoading()
    {
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.Home, Home));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.LoginForm, Home));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.MapLoading, Home));
    }

    [Fact]
    public void For_LoginForm_IsTheLoginFormAndNotHome()
    {
        // LoadingScreenMemory read this form as a menu, so home needed OCR to veto it.
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, Login));
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.LoginForm, Login));
    }

    [Fact]
    public void For_MapLoadingAfterAMenu_IsMapLoading()
    {
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.MapLoading, MapLoading));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, MapLoading));
    }

    [Fact]
    public void For_BootSplash_IsNotHomeButCannotTellMapLoading()
    {
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, Boot));
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.MapLoading, Boot));
    }

    [Fact]
    public void For_Match_IsNoMenuScreen()
    {
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, Match));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.LoginForm, Match));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.MapLoading, Match));
    }

    [Fact]
    public void For_UnknownOrNoSample_CannotTell()
    {
        foreach (ScreenState state in new[] { ScreenState.Home, ScreenState.LoginForm })
        {
            Assert.Null(ScreenMemoryVerdicts.For(state, Unknown));
            Assert.Null(ScreenMemoryVerdicts.For(state, null));
        }
    }

    [Theory]
    [InlineData(ScreenState.GameDataDownload)]
    [InlineData(ScreenState.GameDataStartup)]
    [InlineData(ScreenState.VersionMismatch)]
    [InlineData(ScreenState.RegionUnavailable)]
    [InlineData(ScreenState.EndScreen)]
    public void For_StatesMemoryDoesNotReadYet_CannotTell(ScreenState state)
    {
        foreach (
            ClientScreenSample? sample in new ClientScreenSample?[]
            {
                Home,
                Login,
                Boot,
                MapLoading,
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
    public void Describe_NamesTheScreenReasonShownScreensAndMenuSeen()
    {
        Assert.Equal(
            "Login, screens, shown [BackgroundHero,LoginUnified,HeroCutscene,NavigationHero,ForegroundHero], menu seen True",
            ScreenMemoryVerdicts.Describe(Login)
        );
        Assert.Equal("not read", ScreenMemoryVerdicts.Describe(null));
    }

    private static ClientScreenSample Sample(
        ClientScreenKind kind,
        bool menuSeen,
        params string[] shown
    ) => new(kind, shown, "screens", Current, false, menuSeen);
}
