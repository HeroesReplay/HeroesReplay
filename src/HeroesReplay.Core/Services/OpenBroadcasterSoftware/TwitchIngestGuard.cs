namespace HeroesReplay.Core.Services.OpenBroadcasterSoftware;

/// <summary>
/// Twitch ingest fails closed. <c>services start</c> does not start it, and a stream
/// start is refused unless <c>OBS:StreamingEnabled</c> is true. Only
/// <c>appsettings.prod.json</c> turns streaming on, so the environment
/// (<c>HEROES_REPLAY_ENV</c>) decides, not the machine name.
/// </summary>
public static class TwitchIngestGuard
{
    public const string NotStartedMessage =
        "Twitch ingest was not started. Streaming stays at OBS:StreamingEnabled.";

    public static string Refusal(bool streamingEnabled) =>
        streamingEnabled ? NotStartedMessage : "OBS streaming is disabled.";
}
