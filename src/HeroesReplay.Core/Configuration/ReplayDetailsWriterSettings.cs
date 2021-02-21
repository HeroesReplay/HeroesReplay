namespace HeroesReplay.Core.Configuration
{
    public record ReplayDetailsWriterSettings
    {
        public bool Enabled { get; init; }
        public bool Bans { get; init; }
        public bool GameMode { get; init; }
        public bool Requestor { get; init; }
    }
}