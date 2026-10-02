namespace HeroesReplay.Core.Obs;

/// <summary>
/// Twitch ingest fails closed. <c>services start</c> does not start it, and a stream
/// start is refused unless <c>OBS:StreamingEnabled</c> is true and this machine is armed
/// (<see cref="ObsStreamArm"/>). Only <c>appsettings.prod.json</c> turns streaming on, so the
/// environment (<c>HEROES_REPLAY_ENV</c>) decides, not the machine name. The arm is the
/// second key: it is machine-local and untracked, so the overlay alone cannot go live.
/// </summary>
public static class TwitchIngestGuard
{
    public const string NotStartedMessage =
        "Twitch ingest was not started. The spectator starts it only when OBS:StreamingEnabled is true and this machine is armed (heroesreplay obs arm).";

    public const string NotArmedMessage =
        "OBS:StreamingEnabled is true, but this machine is not armed for Twitch ingest. The stream was not started. Run `heroesreplay obs arm` on the stream PC to allow it.";

    public static string Refusal(bool streamingEnabled, bool armed)
    {
        if (!streamingEnabled)
        {
            return "OBS streaming is disabled.";
        }

        return armed ? NotStartedMessage : NotArmedMessage;
    }

    /// <summary>Stable status code when the settings want a stream this machine may not start.</summary>
    public static string BlockedBy(bool streamingEnabled, bool armed) =>
        streamingEnabled && !armed ? ObsStreamArm.NotArmedReason : null;
}
