using System;

namespace HeroesReplay.Core.GameClient;

/// <summary>
/// The text rules for Battle.net's own errors, applied to the text HeroesClientSDK reads from the
/// client's dialogs in memory (<c>ClientScreenSample.BattlenetError</c> and
/// <c>DialogMessages</c>, #292). The game window is never OCR'd. The client takes these texts from
/// its own tables (2.57.0.98348, 2026-10-08):
/// <list type="bullet">
/// <item>Battle.net error 169: "The selected region is currently unavailable. Please try again
/// later or select another region."</item>
/// <item>Battle.net error 153: "Game client version mismatch with selected region.  You may be able
/// to continue playing if you exit the client, patch from the launcher, and restart.", also under
/// the title "Version Mismatch".</item>
/// <item>A disconnect: "You were disconnected from Blizzard services."
/// (<c>UI/BattleNetErrorDialog/Error_ServiceLost</c>), "Connection Lost"
/// (<c>CDisconnectedDialog</c>), "Connection was unexpectedly lost" and "You were disconnected
/// from the server." (the error table), and the older Battle.net wordings.</item>
/// </list>
/// </summary>
public static class BattleNetErrorText
{
    /// <summary>Battle.net's "Game client version mismatch with selected region." error.</summary>
    public static bool IsVersionMismatch(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && text.Contains("version mismatch", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Battle.net's "The selected region is currently unavailable." error.</summary>
    public static bool IsRegionUnavailable(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains("region", StringComparison.OrdinalIgnoreCase)
            && text.Contains("unavailable", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A disconnect from Battle.net (Blizzard services).</summary>
    public static bool IsDisconnect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (
            text.Contains("disconnected from blizzard services", StringComparison.OrdinalIgnoreCase)
            || text.Contains("disconnected from the server", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection lost", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection was unexpectedly lost", StringComparison.OrdinalIgnoreCase)
            || text.Contains("disconnected from battle.net", StringComparison.OrdinalIgnoreCase)
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
