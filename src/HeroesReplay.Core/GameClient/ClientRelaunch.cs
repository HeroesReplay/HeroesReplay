using System;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// Battle.net drops a Heroes launch that arrives while the previous session is still
/// going off. A second request is sent only when that process never appears.
/// A blank startup window (<see cref="BlankStartupWindow"/>: a uniform full-size frame with no
/// screen in memory yet) is not a menu or a match.
/// </summary>
public static class ClientRelaunch
{
    public static readonly TimeSpan SettleAfterExit = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan SettleAfterBrokenWindow = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan RetryIfNoProcess = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan BlankWindowLimit = TimeSpan.FromMinutes(4);
    public static readonly TimeSpan ColdBootLimit = TimeSpan.FromMinutes(4);
    public static readonly TimeSpan GameDataStartupExtension = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan GameDataStartupCap = TimeSpan.FromMinutes(12);

    public const int MaxLaunchRequests = 2;
    public const int MaxBlankRelaunches = 1;

    public static bool ShouldRequestLaunch(
        bool processRunning,
        int requestsSent,
        TimeSpan sinceLastRequest
    )
    {
        if (processRunning || requestsSent >= MaxLaunchRequests)
        {
            return false;
        }

        if (requestsSent == 0)
        {
            return true;
        }

        return sinceLastRequest >= RetryIfNoProcess;
    }

    /// <summary>
    /// A blank full-size window is the client still starting, or still downloading data.
    /// HeroesSwitcher started that process. It is not closed from this check.
    /// </summary>
    public static bool ShouldRelaunchBlankWindow(
        bool processRunning,
        bool replayOpened,
        bool windowBlank,
        TimeSpan blankFor,
        int blankRelaunches,
        bool clientBuildMatches = true
    )
    {
        _ = processRunning;
        _ = replayOpened;
        _ = windowBlank;
        _ = blankFor;
        _ = blankRelaunches;
        _ = clientBuildMatches;
        return false;
    }

    /// <summary>
    /// A switcher left behind after Heroes exits swallows the next open.
    /// </summary>
    public static bool ShouldCloseSwitcher(bool heroesRunning, bool switcherRunning)
    {
        return switcherRunning && !heroesRunning;
    }

    public static DateTimeOffset DeadlineAfterInterfaceRestart(DateTimeOffset now)
    {
        return now.Add(ColdBootLimit);
    }

    /// <summary>
    /// While the replay's own client is calculating game data, a black full-size window is that same startup.
    /// A matching client that was already running is the same wait: killing it starts the calculation over.
    /// A different build is not that calculation. Extending the wait there leaves the screen idle.
    /// </summary>
    public static bool KeepsWaitingForGameData(
        bool startupText,
        bool sawStartup,
        bool windowBlank,
        bool clientAlreadyRunning,
        bool clientBuildMatches = true,
        TimeSpan blankFor = default
    )
    {
        if (!clientBuildMatches)
        {
            return false;
        }

        if (startupText)
        {
            return true;
        }

        // A black window is the startup calculation only for a short while.
        // After that it is not still preparing, and extending the wait leaves the screen idle.
        if (blankFor >= BlankWindowLimit)
        {
            return false;
        }

        return (sawStartup && windowBlank) || (clientAlreadyRunning && windowBlank);
    }

    /// <summary>
    /// The matching client has a full-size window and no menu, loading, or game-data text
    /// for the whole cold-boot limit. That process is not a match.
    /// A different build is the switcher handoff and stays up.
    /// A signed-in client can stay black for a few minutes before the menu is readable.
    /// </summary>
    public static bool BlankLaunchIsBroken(
        bool processRunning,
        bool windowBlank,
        bool startupOrDownloadVisible,
        TimeSpan blankFor,
        bool clientBuildMatches
    )
    {
        return processRunning
            && clientBuildMatches
            && windowBlank
            && !startupOrDownloadVisible
            && blankFor >= BlankWindowLimit;
    }

    /// <summary>
    /// HeroesSwitcher starts the newest exe, then switches to the replay's build.
    /// That newer process is the handoff. It stays up while the older client's data downloads.
    /// </summary>
    public static bool KeepsWaitingForSwitcherHandoff(
        bool openedThroughSwitcher,
        bool differentBuild,
        bool processRunning
    )
    {
        return openedThroughSwitcher && processRunning && differentBuild;
    }

    /// <summary>
    /// The matching exe was started with the replay path and a different build is what stayed up.
    /// The next replay starts. Another game-data extension would be a gap with no match.
    /// </summary>
    public static bool MatchingOpenLostTheBuild(bool openedOnMatchingExe, bool runningBuildDiffers)
    {
        return openedOnMatchingExe && runningBuildDiffers;
    }

    /// <summary>
    /// The matching exe was started with the replay path and no Heroes process stayed up.
    /// Waiting out the cold-boot limit leaves an empty desktop. The next replay starts.
    /// </summary>
    public static bool MatchingOpenLeftNoProcess(
        bool openedOnMatchingExe,
        bool processRunning,
        TimeSpan sinceOpen
    )
    {
        return openedOnMatchingExe && !processRunning && sinceOpen >= RetryIfNoProcess;
    }

    public static DateTimeOffset ExtendForGameDataStartup(
        DateTimeOffset started,
        DateTimeOffset deadline,
        DateTimeOffset now
    )
    {
        DateTimeOffset cap = started.Add(GameDataStartupCap);
        DateTimeOffset proposed = now.Add(GameDataStartupExtension);
        if (proposed < deadline)
        {
            proposed = deadline;
        }

        if (proposed > cap)
        {
            proposed = cap;
        }

        return proposed;
    }

    public static ClientHoldReason ColdBootHold(
        bool openedFromHome,
        bool replayFileOpened,
        bool sawStartup,
        bool interfaceRestarted,
        bool processRunning,
        bool clientBuildMatches = true
    )
    {
        if (!processRunning || !clientBuildMatches)
        {
            return ClientHoldReason.ClientNotReady;
        }

        // The AhliObs restart opens the replay file and clears the startup flag.
        // That timeout is the new process still coming up, not a match.
        if (interfaceRestarted)
        {
            return ClientHoldReason.ClientNotReady;
        }

        if (openedFromHome)
        {
            return ClientHoldReason.None;
        }

        if (!replayFileOpened || sawStartup)
        {
            return ClientHoldReason.ClientNotReady;
        }

        return ClientHoldReason.None;
    }
}
