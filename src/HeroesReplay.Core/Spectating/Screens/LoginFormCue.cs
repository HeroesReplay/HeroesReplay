namespace HeroesReplay.Core.Spectating.Screens;

/// <summary>
/// The email and password form of a client that was not signed in, from memory only (#292):
/// HeroesClientSDK <c>ClientScreen</c> <c>Login</c>, <c>ScreenLoginUnified</c> with no
/// <c>CLoginDialog</c> over it, so Battle.net's AUTHENTICATION "Connecting..." panel is not the
/// form. Memory that cannot tell is not the form. The window is never OCR'd for it, and the
/// Battle.net disconnect rule never takes a form memory reads (#385).
/// </summary>
public static class LoginFormCue
{
    public static bool Sees(bool? loginInMemory) => loginInMemory == true;
}
