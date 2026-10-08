using HeroesReplay.Core.GameClient;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// The home screen is memory first. HeroesClientSDK's menu screens (<c>ClientScreenMemory</c>)
/// decide when they can tell: only the client's own <c>ScreenHome</c> is home, so the email and
/// password form, Battle.net's AUTHENTICATION "Connecting..." panel, the game-data DOWNLOADING
/// screen and a message dialog are not (#292). Before that, any menu in
/// <c>LoadingScreenMemory</c> was home, and the replay was opened on those screens. When the menu
/// screens cannot tell, that older rule still applies: a menu in memory is home unless OCR reads
/// a login form, and a loading screen or a match in memory is not home. OCR's words decide only
/// when memory cannot tell at all. On 2026-10-02 a Heroes window that was not in front captured
/// black for minutes: memory said menu, OCR saw nothing, and the next replay was never opened.
/// </summary>
public static class HomeScreenCue
{
    public static bool Sees(
        bool? homeInMemory,
        bool? menuInMemory,
        bool ocrFoundHome,
        string ocrText
    )
    {
        if (homeInMemory is bool home)
        {
            return home && !ClientScreenText.IsLoginForm(ocrText);
        }

        if (menuInMemory == true)
        {
            return !ClientScreenText.IsLoginForm(ocrText);
        }

        return menuInMemory == null && ocrFoundHome;
    }
}
