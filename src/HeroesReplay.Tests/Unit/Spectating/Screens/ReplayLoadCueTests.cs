using System;
using HeroesClientSDK;
using HeroesReplay.Core.Spectating.Screens;
using Xunit;

namespace HeroesReplay.Tests.Unit.Spectating.Screens;

[Trait(TestCategories.Category, TestCategories.Unit)]
public class ReplayLoadCueTests
{
    [Fact]
    public void Classify_ProcessAlone_DoesNotSelectTheGameScene()
    {
        NextMatchLaunch launch = ReplayLoadCue.Classify(
            processRunning: true,
            loadingScreen: false,
            hudTimer: null
        );

        Assert.Equal(NextMatchLaunch.ProcessOnly, launch);
        Assert.False(ReplayLoadCue.SelectsGameScene(launch));
        Assert.False(ReplayLoadCue.SelectsWaitingScene(launch));
    }

    [Fact]
    public void Classify_LoadingScreen_SelectsTheGameScene()
    {
        NextMatchLaunch launch = ReplayLoadCue.Classify(
            processRunning: true,
            loadingScreen: true,
            hudTimer: null
        );

        Assert.Equal(NextMatchLaunch.Presented, launch);
        Assert.True(ReplayLoadCue.SelectsGameScene(launch));
        Assert.False(ReplayLoadCue.SelectsWaitingScene(launch));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    [InlineData(-95)]
    public void Classify_HudTimer_SelectsTheGameScene(int seconds)
    {
        NextMatchLaunch launch = ReplayLoadCue.Classify(
            processRunning: true,
            loadingScreen: false,
            hudTimer: TimeSpan.FromSeconds(seconds)
        );

        Assert.Equal(NextMatchLaunch.Presented, launch);
        Assert.True(ReplayLoadCue.SelectsGameScene(launch));
    }

    [Fact]
    public void Classify_NothingRunning_SelectsTheWaitingScene()
    {
        NextMatchLaunch launch = ReplayLoadCue.Classify(
            processRunning: false,
            loadingScreen: false,
            hudTimer: null
        );

        Assert.Equal(NextMatchLaunch.NotStarted, launch);
        Assert.False(ReplayLoadCue.SelectsGameScene(launch));
        Assert.True(ReplayLoadCue.SelectsWaitingScene(launch));
    }

    [Fact]
    public void PresentedInMemory_MatchWithNoClock_IsTheReplayOnScreen()
    {
        // #249: memory said Match (menu seen True), the clock did not read, and the launch
        // waited for a menu. A match in memory is the replay, with or without a clock read.
        var match = new LoadingScreenSample(LoadingScreenKind.Match, MenuSeen: true, "match");

        Assert.True(ReplayLoadCue.PresentedInMemory(clockRunning: false, match));
    }

    [Fact]
    public void PresentedInMemory_RunningClockDecidesWithoutTheScreen()
    {
        Assert.True(ReplayLoadCue.PresentedInMemory(clockRunning: true, screen: null));
        Assert.True(
            ReplayLoadCue.PresentedInMemory(
                clockRunning: true,
                new LoadingScreenSample(LoadingScreenKind.Menu, MenuSeen: true, "menu")
            )
        );
    }

    [Fact]
    public void PresentedInMemory_MenuIsNotPresented_AndUnknownReadsTheScreen()
    {
        Assert.False(
            ReplayLoadCue.PresentedInMemory(
                clockRunning: false,
                new LoadingScreenSample(LoadingScreenKind.Menu, MenuSeen: true, "menu")
            )
        );
        Assert.True(
            ReplayLoadCue.PresentedInMemory(
                clockRunning: false,
                new LoadingScreenSample(LoadingScreenKind.Loading, MenuSeen: true, "loading")
            )
        );
        Assert.Null(ReplayLoadCue.PresentedInMemory(clockRunning: false, screen: null));
        Assert.Null(
            ReplayLoadCue.PresentedInMemory(
                clockRunning: false,
                new LoadingScreenSample(LoadingScreenKind.Loading, MenuSeen: false, "loading")
            )
        );
        Assert.Null(
            ReplayLoadCue.PresentedInMemory(
                clockRunning: false,
                new LoadingScreenSample(LoadingScreenKind.Unknown, MenuSeen: true, "no-state")
            )
        );
    }

    [Fact]
    public void PresentedInMemory_MatchBeforeAnyMenu_LeavesItToTheClockAndClientScreen()
    {
        Assert.Null(
            ReplayLoadCue.PresentedInMemory(
                clockRunning: false,
                new LoadingScreenSample(LoadingScreenKind.Match, MenuSeen: false, "match")
            )
        );
    }

    // ClientScreen reads, 2.57.0.98304 loaded straight from the replay file through HeroesSwitcher
    // (no menu on that process): the boot splash, then the map loading screen (#292).
    private static readonly HeroesClientVersion Previous = new(2, 57, 0, 98304);

    private static readonly ClientScreenSample Splash = new(
        ClientScreenKind.Splash,
        new[] { "ScreenLoading" },
        MenuSeen: false,
        "screens",
        Previous
    );

    private static readonly ClientScreenSample MapPanel = new(
        ClientScreenKind.MapLoading,
        Array.Empty<string>(),
        MenuSeen: false,
        "map-panel",
        Previous
    );

    private static readonly LoadingScreenSample LoadingBeforeMenu = new(
        LoadingScreenKind.Loading,
        MenuSeen: false,
        "loading"
    );

    [Fact]
    public void PresentedInMemory_BeforeAnyMenu_ClientScreenTellsTheMapFromTheBootSplash()
    {
        Assert.True(
            ReplayLoadCue.PresentedInMemory(clockRunning: false, LoadingBeforeMenu, MapPanel)
        );
        Assert.False(
            ReplayLoadCue.PresentedInMemory(clockRunning: false, LoadingBeforeMenu, Splash)
        );
        Assert.True(ReplayLoadCue.PresentedInMemory(clockRunning: false, screen: null, MapPanel));
    }

    [Fact]
    public void PresentedInMemory_LoadingScreenStillDecidesAfterAMenu()
    {
        var menu = new LoadingScreenSample(LoadingScreenKind.Menu, MenuSeen: true, "menu");

        Assert.False(ReplayLoadCue.PresentedInMemory(clockRunning: false, menu, MapPanel));
    }

    [Fact]
    public void PresentedInMemory_NeitherReaderCanTell_IsUnknown()
    {
        var unknown = new ClientScreenSample(
            ClientScreenKind.Unknown,
            Array.Empty<string>(),
            MenuSeen: false,
            "no-state"
        );

        Assert.Null(
            ReplayLoadCue.PresentedInMemory(clockRunning: false, LoadingBeforeMenu, unknown)
        );
        Assert.Null(ReplayLoadCue.PresentedInMemory(clockRunning: false, null, null));
    }

    [Fact]
    public void MapLoadingInMemory_LoadingScreenFirstThenClientScreen()
    {
        var mapAfterMenu = new LoadingScreenSample(
            LoadingScreenKind.Loading,
            MenuSeen: true,
            "loading"
        );
        var menu = new LoadingScreenSample(LoadingScreenKind.Menu, MenuSeen: true, "menu");

        Assert.True(ReplayLoadCue.MapLoadingInMemory(mapAfterMenu, Splash));
        Assert.False(ReplayLoadCue.MapLoadingInMemory(menu, MapPanel));
        Assert.True(ReplayLoadCue.MapLoadingInMemory(LoadingBeforeMenu, MapPanel));
        Assert.False(ReplayLoadCue.MapLoadingInMemory(LoadingBeforeMenu, Splash));
        Assert.Null(ReplayLoadCue.MapLoadingInMemory(LoadingBeforeMenu, null));
        Assert.Null(ReplayLoadCue.MapLoadingInMemory(null, null));
    }
}
