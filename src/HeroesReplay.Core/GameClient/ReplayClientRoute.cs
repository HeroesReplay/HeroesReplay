using System;
using System.Collections.Generic;
using HeroesReplay.Core.HeroesProfile;

namespace HeroesReplay.Core.GameClient;

public enum ReplayClientPatch
{
    Current,
    Previous,
    NotInstalled,
}

public enum RunningClientBuild
{
    None,
    Matches,
    Differs,
    Unreadable,
}

public enum ReplayLaunchAuth
{
    AuthenticateCurrent,
    OpenFromHome,
    OpenInstalledBuild,
    OpenMatchingBuild,
    Wait,
    AlreadyInMatch,
    Unavailable,

    /// <summary>
    /// The current-patch client is playing a different replay than the one this session opens.
    /// Close it and ask the logged-in Battle.net to start Heroes again.
    /// </summary>
    RelaunchCurrent,
}

/// <summary>What a launch does once a matching client has shown nothing it can use for too long.</summary>
public enum LaunchWaitAction
{
    /// <summary>Inside the limit, or the client is busy (game data, a loading screen, a menu).</summary>
    KeepWaiting,

    /// <summary>Current patch: close the client and ask the logged-in Battle.net again.</summary>
    RelaunchCurrent,

    /// <summary>Previous patch: open the replay through HeroesSwitcher again. The client stays open.</summary>
    ReopenThroughSwitcher,

    /// <summary>The one recovery is spent. The launch ends now and the replay stays queued.</summary>
    GiveUp,
}

public enum ReplaySignInRecovery
{
    LaunchCurrent,
    OpenPreviousBuild,
    Leave,
}

/// <summary>
/// The newest installed client signs in through Battle.net, then opens the replay.
/// An older installed build, including an older iteration of the same patch line, is opened by
/// HeroesSwitcher with the .StormReplay path. That is the same open Explorer uses. The process
/// HeroesSwitcher starts is left running while it downloads game data. Battle.net Play always
/// starts the newest client. A different build number is not that client.
/// </summary>
public static class ReplayClientRoute
{
    public static string Normalize(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        return version.Trim().Replace(',', '.').Replace(" ", "", StringComparison.Ordinal);
    }

    public static bool SameBuild(string left, string right)
    {
        string a = Normalize(left);
        string b = Normalize(right);
        return a.Length > 0 && string.Equals(a, b, StringComparison.Ordinal);
    }

    public static ReplayClientPatch Classify(
        string replayVersion,
        IEnumerable<string> installedFileVersions
    )
    {
        string replay = Normalize(replayVersion);
        var installed = new List<string>();
        if (installedFileVersions != null)
        {
            foreach (string version in installedFileVersions)
            {
                string normalized = Normalize(version);
                if (normalized.Length == 0 || installed.Contains(normalized))
                {
                    continue;
                }

                installed.Add(normalized);
            }
        }

        // An unread install folder must not block the current-patch sign-in.
        if (replay.Length == 0 || installed.Count == 0)
        {
            return ReplayClientPatch.Current;
        }

        string newest = installed[0];
        for (int i = 1; i < installed.Count; i++)
        {
            if (!GameVersionOrder.IsAtLeast(newest, installed[i]))
            {
                newest = installed[i];
            }
        }

        if (SameBuild(replay, newest))
        {
            return ReplayClientPatch.Current;
        }

        foreach (string version in installed)
        {
            if (SameBuild(version, replay))
            {
                return ReplayClientPatch.Previous;
            }
        }

        return ReplayClientPatch.NotInstalled;
    }

    /// <summary>
    /// The launch step. <paramref name="replayPresented"/> is the replay on screen on the
    /// matching client: its map loading screen, a match in memory, or a running match clock.
    /// <paramref name="otherReplayOnClient"/> is true when this spectator opened a different
    /// replay on that client, so the match on screen is not this replay's.
    /// </summary>
    public static ReplayLaunchAuth Decide(
        ReplayClientPatch patch,
        RunningClientBuild running,
        bool homeScreen,
        bool replayPresented,
        bool otherReplayOnClient = false
    )
    {
        if (patch == ReplayClientPatch.NotInstalled)
        {
            return ReplayLaunchAuth.Unavailable;
        }

        if (running == RunningClientBuild.Unreadable)
        {
            return ReplayLaunchAuth.Wait;
        }

        if (running == RunningClientBuild.Matches && replayPresented)
        {
            if (!otherReplayOnClient)
            {
                // A replay already playing is a normal start: the report preloaded it.
                return ReplayLaunchAuth.AlreadyInMatch;
            }

            // Another replay is playing. A previous-patch client takes this file through
            // HeroesSwitcher and stays open; the current patch is closed and signed in again.
            return patch == ReplayClientPatch.Previous
                ? ReplayLaunchAuth.OpenMatchingBuild
                : ReplayLaunchAuth.RelaunchCurrent;
        }

        if (running == RunningClientBuild.Matches && homeScreen)
        {
            return ReplayLaunchAuth.OpenFromHome;
        }

        // The matching older exe is already up. Open the .StormReplay through HeroesSwitcher
        // and leave that process running. A direct exe launch is not used.
        if (running == RunningClientBuild.Matches && patch == ReplayClientPatch.Previous)
        {
            return ReplayLaunchAuth.OpenMatchingBuild;
        }

        if (running == RunningClientBuild.Matches)
        {
            return ReplayLaunchAuth.Wait;
        }

        if (patch == ReplayClientPatch.Previous)
        {
            return ReplayLaunchAuth.OpenInstalledBuild;
        }

        return ReplayLaunchAuth.AuthenticateCurrent;
    }

    /// <summary>
    /// The match on the client belongs to another replay only when this spectator opened a
    /// different file on it. Unknown (a spectate restart) is this replay, as before.
    /// </summary>
    public static bool OtherReplayOnClient(string openedOnClient, string replayPath) =>
        !string.IsNullOrWhiteSpace(openedOnClient)
        && !string.IsNullOrWhiteSpace(replayPath)
        && !string.Equals(openedOnClient, replayPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>The default for <c>Spectate:LaunchWaitLimit</c>.</summary>
    public static readonly TimeSpan DefaultLaunchWaitLimit = TimeSpan.FromMinutes(3);

    /// <summary>
    /// A Wait step is bounded (#249). The launch re-checks the client every pass; once the
    /// matching client has shown no menu, loading screen, match, match clock, game data, or
    /// blank startup window for <paramref name="limit"/>, it is recovered once by the patch
    /// rules: the current patch is closed and signed in again through Battle.net, a previous
    /// patch gets the replay through HeroesSwitcher again without closing. After that one
    /// recovery the launch gives up instead of waiting out the cold-boot limit again.
    /// </summary>
    public static LaunchWaitAction DecideStuckWait(
        ReplayClientPatch patch,
        TimeSpan stuckFor,
        TimeSpan limit,
        int recoveriesAlready
    )
    {
        TimeSpan bound = limit > TimeSpan.Zero ? limit : DefaultLaunchWaitLimit;
        if (stuckFor < bound)
        {
            return LaunchWaitAction.KeepWaiting;
        }

        if (recoveriesAlready >= 1 || patch == ReplayClientPatch.NotInstalled)
        {
            return LaunchWaitAction.GiveUp;
        }

        return patch == ReplayClientPatch.Previous
            ? LaunchWaitAction.ReopenThroughSwitcher
            : LaunchWaitAction.RelaunchCurrent;
    }

    public static ReplaySignInRecovery Recover(ReplayClientPatch patch, int attemptsAlready)
    {
        if (attemptsAlready >= 1 || patch == ReplayClientPatch.NotInstalled)
        {
            return ReplaySignInRecovery.Leave;
        }

        if (patch == ReplayClientPatch.Previous)
        {
            return ReplaySignInRecovery.OpenPreviousBuild;
        }

        return ReplaySignInRecovery.LaunchCurrent;
    }

    /// <summary>
    /// The report scenes already handed this replay to HeroesSwitcher and that matching exe is still up.
    /// Opening the file again restarts a client that is already loading.
    /// </summary>
    public static bool OpensTheMatchingBuildAgain(
        ReplayLaunchAuth auth,
        bool sameReplayAlreadyOpened
    )
    {
        return auth == ReplayLaunchAuth.OpenMatchingBuild && !sameReplayAlreadyOpened;
    }

    /// <summary>
    /// A matching-build open returns Wait after the file is opened. That wait still owns the process:
    /// if it exits, the launch ends. An older-build handoff is a different step and keeps its own wait.
    /// </summary>
    public static bool TreatsAsMatchingOpen(ReplayLaunchAuth auth, bool replayFileOpened)
    {
        return auth == ReplayLaunchAuth.OpenMatchingBuild
            || (auth == ReplayLaunchAuth.Wait && replayFileOpened);
    }

    /// <summary>
    /// A direct exe launch is not used. HeroesSwitcher already has the replay path.
    /// </summary>
    public static bool OpenMatchingBuildNow(
        ReplayLaunchAuth auth,
        bool alreadyOpened,
        bool windowBlank,
        bool gameDataStillStarting
    )
    {
        _ = auth;
        _ = alreadyOpened;
        _ = windowBlank;
        _ = gameDataStillStarting;
        return false;
    }
}
