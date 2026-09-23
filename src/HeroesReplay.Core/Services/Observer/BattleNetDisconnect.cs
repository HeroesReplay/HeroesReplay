using System;

namespace HeroesReplay.Core.Services.Observer;

public static class BattleNetDisconnect
{
    public static bool IsShown(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
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
