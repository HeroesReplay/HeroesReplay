namespace HeroesReplay.Core.Configuration
{
    public record TwitchSettings
    {
        public string AccessToken { get; init; }
        public string ClientId { get; init; }
        public bool EnableTwitchClips { get; init; }
        public string Channel { get; init; }
        public string ReplayRequestsFileName { get; init; }
        public bool EnableReplayRequests { get; init; }
    }
}