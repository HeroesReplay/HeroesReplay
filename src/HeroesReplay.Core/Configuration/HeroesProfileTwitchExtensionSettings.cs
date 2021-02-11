namespace HeroesReplay.Core.Configuration
{
    public record HeroesProfileTwitchExtensionSettings
    {
        public bool Enabled { get; init; }
        public string ApiKey { get; init; }
        public string ApiEmail { get; init; }
        public string TwitchUserName { get; init; }
        public string ApiUserId { get; init; }
    }
}