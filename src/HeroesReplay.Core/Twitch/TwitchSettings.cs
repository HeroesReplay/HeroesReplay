using System;

namespace HeroesReplay.Core.Twitch;

public class TwitchSettings
{
    public string Account { get; set; }
    public string AccessToken { get; set; }
    public string ClientId { get; set; }
    public string RefreshToken { get; set; }
    public string Channel { get; set; }

    public bool EnableRequests { get; set; }
    public bool EnablePubSub { get; set; }
    public bool EnableChatBot { get; set; }
    public bool EnablePredictions { get; set; }
    public TimeSpan PredictionWindow { get; set; }

    /// <summary>How long the Blind Rating prediction takes guesses (#411). Twitch allows 30 s to 30 min.</summary>
    public TimeSpan RatingPredictionWindow { get; set; } = TimeSpan.FromMinutes(8);
    public bool DryRunMode { get; set; }

    /// <summary>
    /// Space-separated scopes already granted. When it is not set, <c>services start</c> reads
    /// them from Twitch's token validator; if Twitch cannot be reached they stay unknown and do
    /// not stop startup.
    /// </summary>
    public string GrantedScopes { get; set; }

    public string QueueFileName { get; set; }
    public string FailedFileName { get; set; }
}
