using System;
using HeroesClientSDK;

namespace HeroesReplay.Core.Spectating.Screens;

public enum NextMatchLaunch
{
    NotStarted,
    ProcessOnly,
    Presented,
}

public static class ReplayLoadCue
{
    public static bool IsPresented(bool loadingScreen, TimeSpan? hudTimer) =>
        loadingScreen || hudTimer.HasValue;

    /// <summary>
    /// Whether the replay is on screen, from memory only: the running match clock, a match
    /// (<see cref="LoadingScreenSample.InMatch"/>), or the map loading screen. A match counts even
    /// when the clock does not read yet, so a replay that is already playing is never mistaken
    /// for a client stuck before its menu (#249). When <see cref="LoadingScreen"/> cannot tell
    /// (no menu yet, as on a previous-patch client that loads the replay without a home screen),
    /// HeroesClientSDK <see cref="ClientScreen"/> decides the map loading screen from the
    /// <c>ScreenLoading</c> frame's map panel (#292). Null only when neither can tell; the screen
    /// is never OCR'd for it.
    /// </summary>
    public static bool? PresentedInMemory(
        bool clockRunning,
        LoadingScreenSample? screen,
        ClientScreenSample? client = null
    )
    {
        if (clockRunning)
        {
            return true;
        }

        if (screen is LoadingScreenSample sample && (sample.InMatch || sample.MapLoading != null))
        {
            return sample.InMatch ? true : sample.MapLoading;
        }

        return client?.MapLoading;
    }

    /// <summary>
    /// The map loading screen from memory: <see cref="LoadingScreen"/> when it can tell (after
    /// a menu), else HeroesClientSDK <see cref="ClientScreen"/>, which tells the boot splash from
    /// a map loading screen before any menu too (#292). Null when neither can tell.
    /// </summary>
    public static bool? MapLoadingInMemory(
        LoadingScreenSample? screen,
        ClientScreenSample? client
    ) => screen?.MapLoading ?? client?.MapLoading;

    public static NextMatchLaunch Classify(
        bool processRunning,
        bool loadingScreen,
        TimeSpan? hudTimer
    )
    {
        if (IsPresented(loadingScreen, hudTimer))
        {
            return NextMatchLaunch.Presented;
        }

        if (processRunning)
        {
            return NextMatchLaunch.ProcessOnly;
        }

        return NextMatchLaunch.NotStarted;
    }

    public static bool SelectsGameScene(NextMatchLaunch launch) =>
        launch == NextMatchLaunch.Presented;

    public static bool SelectsWaitingScene(NextMatchLaunch launch) =>
        launch == NextMatchLaunch.NotStarted;
}
