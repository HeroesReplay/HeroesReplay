namespace HeroesReplay.Core.Shared
{
    public record TwitchApiSettings
    {
        public string AccessToken { get; init; }
        public string ClientId { get; init; }
    }
}