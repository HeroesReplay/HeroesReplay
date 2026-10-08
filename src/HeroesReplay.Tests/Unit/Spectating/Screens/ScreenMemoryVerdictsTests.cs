using HeroesClientSDK;
using HeroesReplay.Core.GameClient;
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

    // Battle.net "launch Hero", the first seconds: AUTHENTICATION "Connecting..." over the
    // login screen (a shown CLoginDialog).
    private static readonly ClientScreenSample Authenticating = new(
        ClientScreenKind.Authenticating,
        new[] { "ScreenBackgroundHero", "ScreenLoginUnified" },
        MenuSeen: true,
        "screens",
        Current,
        false,
        Dialogs: new[] { "CLoginDialog" }
    );

    // The boot splash (mask 0x20): the loading screen's map panel is hidden.
    private static readonly ClientScreenSample Boot = Sample(
        ClientScreenKind.Splash,
        menuSeen: false,
        "ScreenLoading"
    );

    // A replay's map loading screen: the loading screen's map panel is shown.
    private static readonly ClientScreenSample MapLoading = Sample(
        ClientScreenKind.MapLoading,
        menuSeen: true,
        "ScreenLoading"
    );

    // 2.57.0.98348, 2026-10-08 16:50, HeroesSwitcher with a 2.57.0.98297 replay: "The version of Heroes of
    // the Storm required to play this game is not available." in a CStandardDialog.
    private static readonly ClientScreenSample VersionDialog = new(
        ClientScreenKind.Dialog,
        new[] { "ScreenBackgroundHero", "ScreenLoginUnified" },
        MenuSeen: true,
        "screens",
        Current,
        false,
        Dialogs: new[] { "CStandardDialog" },
        LaunchResultCode: 23,
        LaunchResult: "GameLaunchUnsupportedNoData"
    );

    private static readonly ClientScreenSample Match = new(
        ClientScreenKind.Match,
        new string[0],
        MenuSeen: true,
        "match",
        Current
    );

    // The MVP screen at the end of a replay: CEndOfGameAwardsPanel shown.
    private static readonly ClientScreenSample Awards = new(
        ClientScreenKind.Awards,
        new string[0],
        MenuSeen: true,
        "awards",
        Current
    );

    private static readonly ClientScreenSample Unknown = new(
        ClientScreenKind.Unknown,
        new string[0],
        MenuSeen: false,
        "unsupported-build",
        Current
    );

    [Fact]
    public void For_Home_IsHomeAndNotTheLoginFormOrMapLoading()
    {
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.Home, Home));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.LoginForm, Home));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.MapLoading, Home));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.VersionMismatch, Home));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.EndScreen, Home));
    }

    [Fact]
    public void For_LoginForm_IsTheLoginFormAndNotHome()
    {
        // LoadingScreen read this form as a menu, so home needed OCR to veto it.
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, Login));
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.LoginForm, Login));
    }

    [Fact]
    public void For_Authenticating_IsNeitherTheLoginFormNorHome()
    {
        // OCR reads "AUTHENTICATION Connecting..." as neither; 0.2.0 called it the login form.
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.LoginForm, Authenticating));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, Authenticating));
    }

    [Fact]
    public void For_MapLoading_IsMapLoading()
    {
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.MapLoading, MapLoading));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, MapLoading));
    }

    [Fact]
    public void For_BootSplash_IsNeitherHomeNorMapLoading()
    {
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, Boot));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.MapLoading, Boot));
    }

    [Fact]
    public void For_TheVersionDialog_IsAVersionMismatchAndNotTheLoginForm()
    {
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.VersionMismatch, VersionDialog));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.LoginForm, VersionDialog));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, VersionDialog));
    }

    [Fact]
    public void For_AnotherMessageDialog_IsNotAVersionMismatch()
    {
        ClientScreenSample other = VersionDialog with
        {
            LaunchResultCode = 2,
            LaunchResult = "GameLaunchReplayOpenFailure",
        };

        Assert.False(ScreenMemoryVerdicts.For(ScreenState.VersionMismatch, other));
    }

    [Fact]
    public void For_TheDownloadDialog_IsTheGameDataDownloadAndNotHome()
    {
        // 2.57.0.98348 handing a 2.57.0.98304 replay to its build (2026-10-08 15:39): DOWNLOADING
        // "All data files must be fully downloaded..." in a CProgressBarDialog. LoadingScreen
        // read it as a menu, so the old home rule opened the replay on it.
        var download = new ClientScreenSample(
            ClientScreenKind.Download,
            new string[0],
            MenuSeen: false,
            "no-screen",
            Current,
            false,
            Dialogs: new[] { "CProgressBarDialog" },
            LaunchResultCode: 0,
            LaunchState: 6
        );

        Assert.True(ScreenMemoryVerdicts.For(ScreenState.GameDataDownload, download));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.Home, download));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.GameDataDownload, Home));
    }

    [Fact]
    public void For_TheMvpScreen_IsTheEndScreen()
    {
        Assert.True(ScreenMemoryVerdicts.For(ScreenState.EndScreen, Awards));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.EndScreen, Match));
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
        foreach (
            ScreenState state in new[]
            {
                ScreenState.Home,
                ScreenState.LoginForm,
                ScreenState.MapLoading,
                ScreenState.VersionMismatch,
                ScreenState.EndScreen,
            }
        )
        {
            Assert.Null(ScreenMemoryVerdicts.For(state, Unknown));
            Assert.Null(ScreenMemoryVerdicts.For(state, null));
        }
    }

    [Theory]
    [InlineData(ScreenState.GameDataStartup)]
    [InlineData(ScreenState.RegionUnavailable)]
    public void For_StatesMemoryDoesNotReadYet_CannotTell(ScreenState state)
    {
        foreach (
            ClientScreenSample? sample in new ClientScreenSample?[]
            {
                Home,
                Login,
                Authenticating,
                Boot,
                MapLoading,
                VersionDialog,
                Match,
                Awards,
                Unknown,
                null,
            }
        )
        {
            Assert.Null(ScreenMemoryVerdicts.For(state, sample));
        }
    }

    [Fact]
    public void For_GameDataStartup_ComesFromTheClientWindowsNotMemory()
    {
        var shown = new GameDataWindowSample(true, "progress dialog #32770 \"Progress\" 404x143");
        var hidden = new GameDataWindowSample(false, "no progress dialog");

        Assert.True(ScreenMemoryVerdicts.For(ScreenState.GameDataStartup, Home, shown));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.GameDataStartup, Boot, hidden));
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.GameDataStartup, Home, null));
        Assert.Equal(
            "windows: progress dialog #32770 \"Progress\" 404x143",
            ScreenMemoryVerdicts.Describe(ScreenState.GameDataStartup, Home, shown)
        );
        Assert.Equal(
            "windows: not read",
            ScreenMemoryVerdicts.Describe(ScreenState.GameDataStartup, Home, null)
        );
    }

    [Fact]
    public void For_OtherStates_IgnoreTheWindowRead()
    {
        var shown = new GameDataWindowSample(true, "progress dialog");

        Assert.True(ScreenMemoryVerdicts.For(ScreenState.Home, Home, shown));
        Assert.False(ScreenMemoryVerdicts.For(ScreenState.GameDataDownload, Home, shown));
        Assert.Null(ScreenMemoryVerdicts.For(ScreenState.RegionUnavailable, Home, shown));
        Assert.Equal(
            ScreenMemoryVerdicts.Describe(Login),
            ScreenMemoryVerdicts.Describe(ScreenState.LoginForm, Login, shown)
        );
    }

    [Fact]
    public void Describe_NamesTheScreenReasonShownScreensMenuSeenDialogsLaunchResultAndBuild()
    {
        Assert.Equal(
            "Login, screens, shown [BackgroundHero,LoginUnified,HeroCutscene,NavigationHero,ForegroundHero], menu seen True, build 2.57.0.98348",
            ScreenMemoryVerdicts.Describe(Login)
        );
        Assert.Equal(
            "Dialog, screens, shown [BackgroundHero,LoginUnified], menu seen True, dialogs [CStandardDialog], launch result 23 GameLaunchUnsupportedNoData, build 2.57.0.98348",
            ScreenMemoryVerdicts.Describe(VersionDialog)
        );
        Assert.Equal("not read", ScreenMemoryVerdicts.Describe(null));
    }

    private static ClientScreenSample Sample(
        ClientScreenKind kind,
        bool menuSeen,
        params string[] shown
    ) => new(kind, shown, menuSeen, "screens", Current);
}
