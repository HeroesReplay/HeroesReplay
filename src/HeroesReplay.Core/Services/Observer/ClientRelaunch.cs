using System;

namespace HeroesReplay.Core.Services.Observer;

/// <summary>
/// Battle.net drops a Heroes launch that arrives while the previous session is still
/// going off. A second request is sent only when that process never appears.
/// A full-size window with no readable text is not a menu or a match.
/// </summary>
public static class ClientRelaunch
{
    public static readonly TimeSpan SettleAfterExit = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan RetryIfNoProcess = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan BlankWindowLimit = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan ColdBootLimit = TimeSpan.FromMinutes(4);

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

    public static bool IsBlankClientWindow(string text, int width, int height)
    {
        return string.IsNullOrWhiteSpace(text) && width >= 1000 && height >= 700;
    }

    public static bool ShouldRelaunchBlankWindow(
        bool processRunning,
        bool replayOpened,
        bool windowBlank,
        TimeSpan blankFor,
        int blankRelaunches
    )
    {
        return processRunning
            && !replayOpened
            && windowBlank
            && blankRelaunches < MaxBlankRelaunches
            && blankFor >= BlankWindowLimit;
    }
}
