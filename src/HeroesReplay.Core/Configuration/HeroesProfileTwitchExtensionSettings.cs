namespace HeroesReplay.Core.Configuration
{
    public record HeroesProfileTwitchExtensionSettings
    {
        public bool Enabled { get; init; }
        public string APIKey { get; init; }
        public string APIEmail { get; init; }
        public string TwitchUserName { get; init; }
        public string ApiUserId { get; init; }

        public string ReplayIdKey { get; init; }
        public string TwitchApiKey { get; init; }
        public string TwitchEmailKey { get; init; }
        public string TwitchUserNameKey { get; init; }
        public string BattleTagKey { get; init; }
        public string TeamKey { get; init; }
        public string UserIdKey { get; init; }
        public string BlizzIdKey { get; init; }
        public string HeroKey { get; init; }
        public string GameTypeKey { get; init; }
        public string GameDateKey { get; init; }
        public string GameMapKey { get; init; }
        public string GameVersionKey { get; init; }
        public string RegionKey { get; init; }
        public string TalentKey { get; init; }
    }
}