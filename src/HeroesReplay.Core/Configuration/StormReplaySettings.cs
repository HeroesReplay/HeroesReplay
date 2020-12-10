namespace HeroesReplay.Core.Shared
{
    public record StormReplaySettings
    {
        public string InfoFileName { get; init; }
        public string WildCard { get; init; }
        public string FileExtension { get; init; }
    }
}