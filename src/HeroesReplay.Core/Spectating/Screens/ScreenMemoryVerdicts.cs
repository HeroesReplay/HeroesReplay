using System.Linq;
using HeroesClientSDK;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// What client memory says about each <see cref="ScreenState"/>, for shadow mode: the
/// HeroesClientSDK menu screens (<see cref="ClientScreen"/>), read by the client's own
/// screen names. Home, the login form and the map loading screen come from memory; the other
/// states are null (memory cannot tell them yet) until a newer SDK reads them.
/// </summary>
public static class ScreenMemoryVerdicts
{
    public static bool? For(ScreenState state, ClientScreenSample? sample) =>
        state switch
        {
            ScreenState.Home => sample?.OnHome,
            ScreenState.LoginForm => sample?.OnLogin,
            ScreenState.MapLoading => sample?.MapLoading,
            _ => null,
        };

    /// <summary>The memory read behind a verdict, for the shadow log.</summary>
    public static string Describe(ClientScreenSample? sample) =>
        sample is ClientScreenSample read
            ? $"{read.Screen}, {read.Reason}, shown [{string.Join(",", (read.Shown ?? new string[0]).Select(Short))}], menu seen {read.MenuSeen}"
            : "not read";

    private static string Short(string screen) =>
        screen != null && screen.StartsWith("Screen", System.StringComparison.Ordinal)
            ? screen.Substring("Screen".Length)
            : screen;
}
