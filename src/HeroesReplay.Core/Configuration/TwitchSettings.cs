using System;

namespace HeroesReplay.Core.Configuration;

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
    public bool DryRunMode { get; set; }

    /// <summary>Space-separated scopes already granted. Startup does not call Twitch to discover them.</summary>
    public string GrantedScopes { get; set; }

    public string QueueFileName { get; set; }
    public string FailedFileName { get; set; }
}
