namespace HeroesReplay.Core.Configuration
{
    public record TwitchApiSettings
    {
        public string AccessToken { get; init; }
        public string ClientId { get; init; }
        public bool EnableTwitchClips { get; init; }
    }
}