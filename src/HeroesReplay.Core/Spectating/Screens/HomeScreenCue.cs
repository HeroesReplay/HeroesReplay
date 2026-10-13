namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// The home screen, from memory only (#292). HeroesClientSDK's menu screens (<c>ClientScreen</c>)
/// decide when they can tell: only the client's own <c>ScreenHome</c> is home, so the email and
/// password form, Battle.net's AUTHENTICATION "Connecting..." panel, the game-data DOWNLOADING
/// dialog and a message dialog are not. When the menu screens cannot tell, a menu in
/// <c>LoadingScreen</c> is home (the rule before #292; a client opens a replay file without
/// signing in, so the replay still loads), and a loading screen, a match, or memory that cannot
/// tell at all is not home. The window is never OCR'd for the home screen. On 2026-10-02 a Heroes
/// window that was not in front captured black for minutes; memory still opens the replay there.
/// </summary>
public static class HomeScreenCue
{
    public static bool Sees(bool? homeInMemory, bool? menuInMemory) =>
        homeInMemory ?? menuInMemory == true;
}
