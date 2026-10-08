using System;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// Battle.net's disconnect dialog, from the window text. The email and password form of a client
/// started without SSO also says "Battle.net" and "Log in", so text the login-form rule matches is
/// never a disconnect, and a login form that memory reads (HeroesClientSDK <c>ClientScreen</c>
/// <c>Login</c>) is never a disconnect either (#385). That form goes through the login-form path:
/// close the client and ask Battle.net again once.
/// </summary>
public static class BattleNetDisconnect
{
    /// <summary>
    /// The disconnect dialog, unless memory reads the login form
    /// (<paramref name="loginInMemory"/> true).
    /// </summary>
    public static bool IsShown(string text, bool? loginInMemory) =>
        loginInMemory != true && IsShown(text);

    public static bool IsShown(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || ClientScreenText.IsLoginForm(text))
        {
            return false;
        }

        if (
            text.Contains("disconnected from battle.net", StringComparison.OrdinalIgnoreCase)
            || text.Contains("disconnected from battlenet", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection to battle.net", StringComparison.OrdinalIgnoreCase)
            || text.Contains("lost connection to battle.net", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unable to connect", StringComparison.OrdinalIgnoreCase)
            || text.Contains(
                "battle.net is currently unavailable",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return true;
        }

        bool mentionsBattleNet =
            text.Contains("battle.net", StringComparison.OrdinalIgnoreCase)
            || text.Contains("battlenet", StringComparison.OrdinalIgnoreCase);
        if (!mentionsBattleNet)
        {
            return false;
        }

        return text.Contains("reconnect", StringComparison.OrdinalIgnoreCase)
            || text.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
            || text.Contains("log in", StringComparison.OrdinalIgnoreCase);
    }
}
