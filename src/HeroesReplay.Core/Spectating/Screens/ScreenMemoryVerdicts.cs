using System;
using System.Linq;
using HeroesClientSDK;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// What client memory says about each <see cref="ScreenState"/>, for shadow mode: the
/// HeroesClientSDK menu screens (<see cref="ClientScreen"/>), read by the client's own
/// screen, dialog and frame class names. Home, the login form, the map loading screen, the game
/// data DOWNLOADING dialog, the version dialogs and the MVP screen come from memory; the other
/// states are null (memory cannot tell them yet).
/// </summary>
public static class ScreenMemoryVerdicts
{
    /// <summary>
    /// The game-launch results whose message dialog OCR reads as a version mismatch: the replay's
    /// build cannot be started or downloaded, or its data build does not match. "The version of
    /// Heroes of the Storm required to play this game is not available." is
    /// <c>GameLaunchUnsupportedNoData</c> (result 23): read live on 2.57.0.98348 with a
    /// 2.57.0.98297 replay on 2026-10-08. The others are the client's other version messages.
    /// </summary>
    public static readonly string[] VersionResults =
    {
        "GameLaunchBaseBuildMissing",
        "GameLaunchVersionDownloadFailure",
        "GameLaunchVersionLaunchError",
        "GameLaunchDataBuildNumMismatch",
        "GameLaunchUnsupportedNoData",
    };

    public static bool? For(ScreenState state, ClientScreenSample? sample) =>
        state switch
        {
            ScreenState.Home => sample?.OnHome,
            ScreenState.LoginForm => sample?.OnLogin,
            ScreenState.MapLoading => sample?.MapLoading,
            ScreenState.EndScreen => sample?.OnAwards,
            ScreenState.GameDataDownload => sample?.OnDownload,
            ScreenState.VersionMismatch => VersionDialog(sample),
            _ => null,
        };

    /// <summary>
    /// True when a message dialog shows a version game-launch result, false on any other known
    /// screen, null when memory cannot tell.
    /// </summary>
    public static bool? VersionDialog(ClientScreenSample? sample)
    {
        if (sample is not ClientScreenSample read || !read.Ok)
        {
            return null;
        }

        return read.Screen == ClientScreenKind.Dialog
            && read.LaunchResult != null
            && VersionResults.Contains(read.LaunchResult, StringComparer.Ordinal);
    }

    /// <summary>The memory read behind a verdict, for the shadow log.</summary>
    public static string Describe(ClientScreenSample? sample)
    {
        if (sample is not ClientScreenSample read)
        {
            return "not read";
        }

        string text =
            $"{read.Screen}, {read.Reason}, shown [{string.Join(",", (read.Shown ?? Array.Empty<string>()).Select(Short))}], menu seen {read.MenuSeen}";
        if (read.Dialogs is { Count: > 0 } dialogs)
        {
            text += $", dialogs [{string.Join(",", dialogs)}]";
        }

        if (read.LaunchResultCode is int code && code != 0)
        {
            text += $", launch result {code} {read.LaunchResult}";
        }

        return text;
    }

    private static string Short(string screen) =>
        screen != null && screen.StartsWith("Screen", StringComparison.Ordinal)
            ? screen.Substring("Screen".Length)
            : screen;
}
