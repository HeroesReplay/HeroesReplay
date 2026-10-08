namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// A client screen the spectator decides on. OCR still reads some of these; shadow mode
/// (<see cref="ScreenShadow"/>) logs the memory verdict next to each OCR verdict.
/// </summary>
public enum ScreenState
{
    Home,
    LoginForm,
    MapLoading,
    GameDataDownload,
    GameDataStartup,
    VersionMismatch,
    RegionUnavailable,
    EndScreen,
}
