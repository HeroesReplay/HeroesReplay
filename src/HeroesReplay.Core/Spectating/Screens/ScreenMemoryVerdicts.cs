using HeroesClientSDK;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// What client memory says about each <see cref="ScreenState"/>, for shadow mode. This is the one
/// place a newer HeroesClientSDK extends (a screen kind, a signed-in flag, dialogs, download
/// state). Null means memory cannot tell that state yet.
/// </summary>
public static class ScreenMemoryVerdicts
{
    public static bool? For(ScreenState state, LoadingScreenSample? sample) =>
        state switch
        {
            ScreenState.Home => sample?.OnMenu,
            ScreenState.MapLoading => sample?.MapLoading,
            _ => null,
        };

    /// <summary>The memory read behind a verdict, for the shadow log.</summary>
    public static string Describe(LoadingScreenSample? sample) =>
        sample is LoadingScreenSample read
            ? $"{read.Screen}, {read.Reason}, menu seen {read.MenuSeen}"
            : "not read";
}
