using System;
using System.Collections.Generic;

namespace HeroesReplay.Core.Spectating.Screens;

public enum NextMatchLaunch
{
    NotStarted,
    ProcessOnly,
    Presented,
}

public static class ReplayLoadCue
{
    public const int MinimumTermLength = 3;

    public static bool IsPresented(bool loadingScreen, TimeSpan? hudTimer) =>
        loadingScreen || hudTimer.HasValue;

    /// <summary>
    /// Whether the replay is on screen, from memory only: the running match clock, a match
    /// (<see cref="LoadingScreenSample.InMatch"/>), or the map loading screen. A match counts even
    /// when the clock does not read yet, so a replay that is already playing is never mistaken
    /// for a client stuck before its menu (#249). Null when memory cannot tell, and only then is
    /// the screen OCR'd for the loading screen text.
    /// </summary>
    public static bool? PresentedInMemory(bool clockRunning, LoadingScreenSample? screen)
    {
        if (clockRunning)
        {
            return true;
        }

        if (screen is not LoadingScreenSample sample)
        {
            return null;
        }

        return sample.InMatch ? true : sample.MapLoading;
    }

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

    public static bool SeesLoadingScreen(
        string windowText,
        string map,
        string mapAlternative,
        IEnumerable<string> playerNames,
        IEnumerable<string> heroNames,
        IEnumerable<string> loadingScreenText
    )
    {
        if (string.IsNullOrWhiteSpace(windowText))
        {
            return false;
        }

        foreach (
            string term in Terms(map, mapAlternative, playerNames, heroNames, loadingScreenText)
        )
        {
            if (windowText.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Terms(
        string map,
        string mapAlternative,
        IEnumerable<string> playerNames,
        IEnumerable<string> heroNames,
        IEnumerable<string> loadingScreenText
    )
    {
        foreach (string term in One(map))
        {
            yield return term;
        }

        foreach (string term in One(mapAlternative))
        {
            yield return term;
        }

        foreach (string term in Many(playerNames))
        {
            yield return term;
        }

        foreach (string term in Many(heroNames))
        {
            yield return term;
        }

        foreach (string term in Many(loadingScreenText))
        {
            yield return term;
        }
    }

    private static IEnumerable<string> One(string term)
    {
        if (TryKeep(term, out string kept))
        {
            yield return kept;
        }
    }

    private static IEnumerable<string> Many(IEnumerable<string> terms)
    {
        if (terms == null)
        {
            yield break;
        }

        foreach (string term in terms)
        {
            if (TryKeep(term, out string kept))
            {
                yield return kept;
            }
        }
    }

    private static bool TryKeep(string term, out string kept)
    {
        kept = term?.Trim();
        return !string.IsNullOrEmpty(kept) && kept.Length >= MinimumTermLength;
    }
}
