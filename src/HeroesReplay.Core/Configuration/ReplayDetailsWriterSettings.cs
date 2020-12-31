namespace HeroesReplay.Core.Shared
{
    public record ReplayDetailsWriterSettings
    {
        public bool Enabled { get; init; }
        public bool Bans { get; init; }
        public bool GameMode { get; init; }
    }
}