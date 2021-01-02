namespace HeroesReplay.Core.Configuration
{
    public record StormReplaySettings
    {
        public string InfoFileName { get; init; }
        public string WildCard { get; init; }
        public string FileExtension { get; init; }
        public string Seperator { get; init; }
    }
}