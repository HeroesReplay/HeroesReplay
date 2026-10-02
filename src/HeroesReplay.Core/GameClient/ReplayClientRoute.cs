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

    public static ReplayLaunchAuth Decide(
        ReplayClientPatch patch,
        RunningClientBuild running,
        bool homeScreen,
        bool replayPresented
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
            return ReplayLaunchAuth.AlreadyInMatch;
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
