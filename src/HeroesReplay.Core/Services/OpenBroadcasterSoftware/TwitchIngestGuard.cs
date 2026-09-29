using System;

namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

/// <summary>
/// Twitch ingest fails closed. <c>services start</c> does not start it, and a stream
/// start is refused unless this is the production host and streaming is enabled.
/// </summary>
public static class TwitchIngestGuard
{
    public const string DevelopmentHost = "ASA-SERVER";
    public const string ProductionHost = "DESKTOP-8SJE72";
    public const string NotStartedMessage =
        "Twitch ingest was not started. Streaming stays at OBS:StreamingEnabled.";

    public static bool Allows(string hostName, bool streamingEnabled)
    {
        if (!streamingEnabled)
        {
            return false;
        }

        return IsProductionHost(hostName);
    }

    public static bool IsProductionHost(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return false;
        }

        return string.Equals(hostName.Trim(), ProductionHost, StringComparison.OrdinalIgnoreCase);
    }

    public static string Refusal(string hostName, bool streamingEnabled)
    {
        if (!streamingEnabled)
        {
            return "OBS streaming is disabled.";
        }

        if (string.Equals(hostName?.Trim(), DevelopmentHost, StringComparison.OrdinalIgnoreCase))
        {
            return NotStartedMessage;
        }

        return NotStartedMessage;
    }
}
