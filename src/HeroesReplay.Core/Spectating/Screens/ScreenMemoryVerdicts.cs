using System;
using System.Linq;
using HeroesClientSDK;
using HeroesReplay.Core.GameClient;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// What client memory says about each <see cref="ScreenState"/>, for shadow mode: the
/// HeroesClientSDK menu screens (<see cref="ClientScreen"/>), read by the client's own
/// screen, dialog and frame class names. Home, the login form, the map loading screen, the game
/// data DOWNLOADING dialog, the version dialogs and the MVP screen come from memory. "Preparing
/// game data" is a native Win32 dialog, not client UI, so its verdict comes from the client's
/// windows (<see cref="GameDataProgressWindow"/>). The other states are null (neither can tell
/// them yet).
/// </summary>
public static class ScreenMemoryVerdicts
{
    public static bool? For(ScreenState state, ClientScreenSample? sample) =>
        state switch
        {
            ScreenState.Home => sample?.OnHome,
            ScreenState.LoginForm => sample?.OnLogin,
            ScreenState.MapLoading => sample?.MapLoading,
            ScreenState.EndScreen => sample?.OnAwards,
            ScreenState.GameDataDownload => sample?.OnDownload,
            ScreenState.VersionMismatch => ClientLaunchFailure.ShowsVersion(sample),
            ScreenState.RegionUnavailable => RegionVerdict(sample),
            _ => null,
        };

    /// <summary>
    /// The verdict for <paramref name="state"/> from memory, or from the client's windows for
    /// "Preparing game data" (<see cref="ScreenState.GameDataStartup"/>).
    /// </summary>
    public static bool? For(
        ScreenState state,
        ClientScreenSample? sample,
        GameDataWindowSample? window
    ) => state == ScreenState.GameDataStartup ? window?.Shown : For(state, sample);

    /// <summary>The read behind <see cref="For(ScreenState, ClientScreenSample?, GameDataWindowSample?)"/>, for the shadow log.</summary>
    public static string Describe(
        ScreenState state,
        ClientScreenSample? sample,
        GameDataWindowSample? window
    ) =>
        state == ScreenState.GameDataStartup
            ? "windows: " + (window?.Reason ?? "not read")
            : Describe(sample);

    /// <summary>
    /// The region verdict: a game-launch failure with a region result
    /// (<see cref="ClientLaunchFailure.RegionResults"/>). Unknown while no region result key is
    /// known, so shadow mode does not count "false" for a dialog memory cannot name.
    /// </summary>
    private static bool? RegionVerdict(ClientScreenSample? sample) =>
        ClientLaunchFailure.RegionResults.Count == 0
            ? null
            : ClientLaunchFailure.ShowsRegion(sample);

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

        // The build tells which client a shadow observation came from during a handoff, when the
        // newest exe and the replay's older build run side by side.
        if (read.ClientVersion != null)
        {
            text += $", build {read.ClientVersion}";
        }

        return text;
    }

    private static string Short(string screen) =>
        screen != null && screen.StartsWith("Screen", StringComparison.Ordinal)
            ? screen.Substring("Screen".Length)
            : screen;
}
