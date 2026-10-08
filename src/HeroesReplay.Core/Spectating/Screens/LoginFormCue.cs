using HeroesReplay.Core.GameClient;

namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// The email and password form of a client that was not signed in. Memory decides when it can
/// tell (HeroesClientSDK <c>ClientScreen</c> <c>Login</c>: <c>ScreenLoginUnified</c> with no
/// <c>CLoginDialog</c> over it, so Battle.net's AUTHENTICATION "Connecting..." panel is not the
/// form); OCR's words count only when memory cannot tell (#292, #385).
/// </summary>
public static class LoginFormCue
{
    public static bool Sees(bool? loginInMemory, string ocrText) =>
        loginInMemory ?? ClientScreenText.IsLoginForm(ocrText);
}
