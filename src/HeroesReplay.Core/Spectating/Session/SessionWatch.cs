namespace HeroesReplay.Core.Spectating.Session;

/// <summary>
/// A hung window is ignored while the match clock is moving forward.
/// </summary>
public static class SessionWatch
{
    public static bool IsHung(bool windowHung, bool clockAdvanced) => windowHung && !clockAdvanced;
}
