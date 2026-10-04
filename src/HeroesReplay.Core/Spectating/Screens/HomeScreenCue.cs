using HeroesReplay.Core.GameClient;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// The home screen is memory first. A menu in memory is home unless OCR reads a login form;
/// a loading screen or a match in memory is not home. OCR's words decide only when memory
/// cannot tell. On 2026-10-02 a Heroes window that was not in front captured black for
/// minutes: memory said menu, OCR saw nothing, and the next replay was never opened.
/// </summary>
public static class HomeScreenCue
{
    public static bool Sees(bool? menuInMemory, bool ocrFoundHome, string ocrText)
    {
        if (menuInMemory == true)
        {
            return !ClientScreenText.IsLoginForm(ocrText);
        }

        return menuInMemory == null && ocrFoundHome;
    }
}
