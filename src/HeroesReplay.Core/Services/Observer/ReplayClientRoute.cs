using System;
using System.Collections.Generic;
using HeroesReplay.Core.Services.HeroesProfile;

namespace HeroesReplay.Core.Services.Observer;

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
/// An older installed build is started by HeroesSwitcher with the replay file.
/// Battle.net Play always starts the newest client.
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
}
